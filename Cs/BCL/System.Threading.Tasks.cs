using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Sources;

namespace System.Threading.Tasks
{
    /// <summary>
    /// Represents the current stage in the lifecycle of a <see cref="Task"/>.
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// The task has been initialized but has not yet been scheduled.
        /// </summary>
        Created,
        /// <summary>
        /// The task is waiting to be activated and scheduled internally by the .NET Framework infrastructure.
        /// </summary>
        WaitingForActivation,
        /// <summary>
        /// The task has been scheduled for execution but has not yet begun executing.
        /// </summary>
        WaitingToRun,
        /// <summary>
        /// The task is running but has not yet completed.
        /// </summary>
        Running,
        /// <summary>
        /// The task has finished executing and is implicitly waiting for
        /// attached child tasks to complete.
        /// </summary>
        WaitingForChildrenToComplete,
        /// <summary>
        /// The task completed execution successfully.
        /// </summary>
        RanToCompletion,
        /// <summary>
        /// The task acknowledged cancellation by throwing an OperationCanceledException with its own CancellationToken
        /// while the token was in signaled state, or the task's CancellationToken was already signaled before the
        /// task started executing.
        /// </summary>
        Canceled,
        /// <summary>
        /// The task completed due to an unhandled exception.
        /// </summary>
        Faulted
    }

    /// <summary>
    /// Represents an asynchronous operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Task"/> instances may be created in a variety of ways. The most common approach is by
    /// using the Task type's <see cref="Factory"/> property to retrieve a <see
    /// cref="TaskFactory"/> instance that can be used to create tasks for several
    /// purposes. For example, to create a <see cref="Task"/> that runs an action, the factory's StartNew
    /// method may be used:
    /// <code>
    /// // C#
    /// var t = Task.Factory.StartNew(() => DoAction());
    ///
    /// ' Visual Basic
    /// Dim t = Task.Factory.StartNew(Function() DoAction())
    /// </code>
    /// </para>
    /// <para>
    /// The <see cref="Task"/> class also provides constructors that initialize the Task but that do not
    /// schedule it for execution. For performance reasons, TaskFactory's StartNew method should be the
    /// preferred mechanism for creating and scheduling computational tasks, but for scenarios where creation
    /// and scheduling must be separated, the constructors may be used, and the task's <see cref="Start()"/>
    /// method may then be used to schedule the task for execution at a later time.
    /// </para>
    /// <para>
    /// All members of <see cref="Task"/>, except for <see cref="Dispose()"/>, are thread-safe
    /// and may be used from multiple threads concurrently.
    /// </para>
    /// <para>
    /// For operations that return values, the <see cref="Task{TResult}"/> class
    /// should be used.
    /// </para>
    /// <para>
    /// For developers implementing custom debuggers, several internal and private members of Task may be
    /// useful (these may change from release to release). The Int32 m_taskId field serves as the backing
    /// store for the <see cref="Id"/> property, however accessing this field directly from a debugger may be
    /// more efficient than accessing the same value through the property's getter method (the
    /// s_taskIdCounter Int32 counter is used to retrieve the next available ID for a Task). Similarly, the
    /// Int32 m_stateFlags field stores information about the current lifecycle stage of the Task,
    /// information also accessible through the <see cref="Status"/> property. The m_action System.Object
    /// field stores a reference to the Task's delegate, and the m_stateObject System.Object field stores the
    /// async state passed to the Task by the developer. Finally, for debuggers that parse stack frames, the
    /// InternalWait method serves a potential marker for when a Task is entering a wait operation.
    /// </para>
    /// </remarks>
    public class Task : IAsyncResult, IDisposable
    {
        [ThreadStatic]
        internal static Task? t_currentTask;  // The currently executing task.

        private static int s_taskIdCounter; // static counter used to generate unique task IDs

        private int m_taskId; // this task's unique ID. initialized only if it is ever requested

        // The delegate to invoke for a delegate-backed Task.
        // This field also may be used by async state machines to cache an Action.
        internal Delegate? m_action;
        private protected object? m_stateObject; // A state object that can be optionally supplied, passed to action.
        internal TaskScheduler? m_taskScheduler; // The task scheduler this task runs under.

        internal volatile int m_stateFlags; // SOS DumpAsync command depends on this name

        private Task? ParentForDebugger => m_contingentProperties?.m_parent; // Private property used by a debugger to access this Task's parent

        [Flags]
        internal enum TaskStateFlags
        {
            // State constants for m_stateFlags;
            // The bits of m_stateFlags are allocated as follows:
            //   0x7FFF0000 - Task state flags
            //   0x0000FF00 - internal TaskCreationOptions flags
            //   0x000000FF - publicly exposed TaskCreationOptions flags
            // See TaskCreationOptions for bit values associated with TaskCreationOptions

            Started = 0x10000,                       // bin: 0000 0000 0000 0001 0000 0000 0000 0000
            DelegateInvoked = 0x20000,               // bin: 0000 0000 0000 0010 0000 0000 0000 0000
            Disposed = 0x40000,                      // bin: 0000 0000 0000 0100 0000 0000 0000 0000
            ExceptionObservedByParent = 0x80000,     // bin: 0000 0000 0000 1000 0000 0000 0000 0000
            CancellationAcknowledged = 0x100000,     // bin: 0000 0000 0001 0000 0000 0000 0000 0000
            Faulted = 0x200000,                      // bin: 0000 0000 0010 0000 0000 0000 0000 0000
            Canceled = 0x400000,                     // bin: 0000 0000 0100 0000 0000 0000 0000 0000
            WaitingOnChildren = 0x800000,            // bin: 0000 0000 1000 0000 0000 0000 0000 0000
            RanToCompletion = 0x1000000,             // bin: 0000 0001 0000 0000 0000 0000 0000 0000
            WaitingForActivation = 0x2000000,        // bin: 0000 0010 0000 0000 0000 0000 0000 0000
            CompletionReserved = 0x4000000,          // bin: 0000 0100 0000 0000 0000 0000 0000 0000
            WaitCompletionNotification = 0x10000000, // bin: 0001 0000 0000 0000 0000 0000 0000 0000
            ExecutionContextIsNull = 0x20000000,     // bin: 0010 0000 0000 0000 0000 0000 0000 0000
            TaskScheduledWasFired = 0x40000000,      // bin: 0100 0000 0000 0000 0000 0000 0000 0000

            CompletedMask = Canceled | Faulted | RanToCompletion, // A mask for all of the final states a task may be in. SOS DumpAsync command depends on these values.
            OptionsMask = 0xFFFF,                    // signifies the Options portion of m_stateFlags bin: 0000 0000 0000 0000 1111 1111 1111 1111
        }

        // Values for ContingentProperties.m_internalCancellationRequested.
        private const int CANCELLATION_REQUESTED = 0x1;

        // Can be null, a single continuation, a list of continuations, or s_taskCompletionSentinel,
        // in that order. The logic arround this object assumes it will never regress to a previous state.
        private volatile object? m_continuationObject; // SOS DumpAsync command depends on this name

        // m_continuationObject is set to this when the task completes.
        private static readonly object s_taskCompletionSentinel = new object();

        // A private flag that would be set (only) by the debugger
        // When true the Async Causality logging trace is enabled as well as a dictionary to relate operation ids with Tasks
        // The debugger sets this field together with the AsyncInstrumentation.Flags.Synchronize bit in
        // AsyncInstrumentation's active flags (this field first, then the Synchronize bit) so the next instrumentation
        // checkpoint re-syncs and observes the AsyncDebugger client. Do not change this field's name or the way the
        // Synchronize bit is composed without coordinating with the debugger; see AsyncInstrumentation.SynchronizeFlags.
        internal static bool s_asyncDebuggingEnabled; // false by default

        // This dictionary relates the task id, from an operation id located in the Async Causality log to the actual
        // task. This is to be used by the debugger ONLY. Task in this dictionary represent current active tasks.
        private static Dictionary<int, Task>? s_currentActiveTasks;

#if !MONO
        // Dictionary that relates a runtime-async Task's ID to the timestamp when the current inflight invocation started.
        // Needed because Continuations that are inflight have already been dequeued from the chain.
        private static Dictionary<int, long>? s_runtimeAsyncTaskTimestamps;
        // Dictionary to store the timestamp when the logical invocation to which the Continuation belongs started.
        private static Dictionary<object, long>? s_runtimeAsyncContinuationTimestamps;
#endif

        // These methods are a way to access the dictionary both from this class and for other classes that also
        // activate dummy tasks. Specifically the AsyncTaskMethodBuilder and AsyncTaskMethodBuilder<>
        internal static bool AddToActiveTasks(Task task)
        {
            Dictionary<int, Task> activeTasks =
                Volatile.Read(ref s_currentActiveTasks) ??
                Interlocked.CompareExchange(ref s_currentActiveTasks, new Dictionary<int, Task>(), null) ??
                s_currentActiveTasks;

            int taskId = task.Id;
            lock (activeTasks)
            {
                activeTasks[taskId] = task;
            }
            // always return true to keep signature as bool for backwards compatibility
            return true;
        }

        internal static void RemoveFromActiveTasks(Task task)
        {
            Dictionary<int, Task>? activeTasks = s_currentActiveTasks;
            if (activeTasks is null)
                return;

            int taskId = task.Id;
            lock (activeTasks)
            {
                activeTasks.Remove(taskId);
            }
        }

#if !MONO
        private static Dictionary<object, long> GetOrCreateRuntimeAsyncContinuationTimestamps()
        {
            return Volatile.Read(ref s_runtimeAsyncContinuationTimestamps) ??
                Interlocked.CompareExchange(ref s_runtimeAsyncContinuationTimestamps, new Dictionary<object, long>(ReferenceEqualityComparer.Instance), null) ??
                s_runtimeAsyncContinuationTimestamps!;
        }

        private static Dictionary<int, long> GetOrCreateRuntimeAsyncTaskTimestamps()
        {
            return Volatile.Read(ref s_runtimeAsyncTaskTimestamps) ??
                Interlocked.CompareExchange(ref s_runtimeAsyncTaskTimestamps, new Dictionary<int, long>(), null) ??
                s_runtimeAsyncTaskTimestamps!;
        }

        internal static void ReplaceOrAddRuntimeAsyncContinuationTimestamp(Continuation curContinuation, Continuation newContinuation)
        {
            var continuationTimestamps = GetOrCreateRuntimeAsyncContinuationTimestamps();
            lock (continuationTimestamps)
            {
                if (continuationTimestamps.TryGetValue(curContinuation, out long timestampVal))
                {
                    continuationTimestamps.Remove(curContinuation);
                    continuationTimestamps[newContinuation] = timestampVal;
                }
                else
                {
                    continuationTimestamps.TryAdd(newContinuation, Stopwatch.GetTimestamp());
                }
            }
        }

        internal static void TryAddRuntimeAsyncContinuationChainTimestamps(Continuation continuationChain)
        {
            var continuationTimestamps = GetOrCreateRuntimeAsyncContinuationTimestamps();
            long timestamp = Stopwatch.GetTimestamp();
            lock (continuationTimestamps)
            {
                Continuation? nc = continuationChain;
                while (nc != null)
                {
                    continuationTimestamps.TryAdd(nc, timestamp);
                    nc = nc.Next;
                }
            }
        }

        internal static void RemoveRuntimeAsyncContinuationTimestamp(Continuation continuation)
        {
            var continuationTimestamps = s_runtimeAsyncContinuationTimestamps;
            if (continuationTimestamps is null)
                return;

            lock (continuationTimestamps)
            {
                continuationTimestamps.Remove(continuation);
            }
        }

        internal static void RemoveRuntimeAsyncContinuationChainTimestamps(Continuation continuation, uint count)
        {
            var continuationTimestamps = s_runtimeAsyncContinuationTimestamps;
            if (continuationTimestamps is null)
                return;

            lock (continuationTimestamps)
            {
                Continuation? nc = continuation;
                for (uint i = 0; i < count && nc != null; i++)
                {
                    continuationTimestamps.Remove(nc);
                    nc = nc.Next;
                }
            }
        }

        internal static void UpdateRuntimeAsyncTaskTimestamp(Task task, Continuation timestampSource)
        {
            long timestamp = 0;
            var continuationTimestamps = s_runtimeAsyncContinuationTimestamps;
            if (continuationTimestamps != null)
            {
                lock (continuationTimestamps)
                {
                    continuationTimestamps.TryGetValue(timestampSource, out timestamp);
                }
            }

            if (timestamp == 0)
            {
                timestamp = Stopwatch.GetTimestamp();
            }

            var runtimeAsyncTaskTimestamps = GetOrCreateRuntimeAsyncTaskTimestamps();
            lock (runtimeAsyncTaskTimestamps)
            {
                runtimeAsyncTaskTimestamps[task.Id] = timestamp;
            }
        }

        internal static void RemoveRuntimeAsyncTask(Task task)
        {
            RemoveFromActiveTasks(task);

            Dictionary<int, long>? runtimeAsyncTaskTimestamps = s_runtimeAsyncTaskTimestamps;
            if (runtimeAsyncTaskTimestamps is null)
                return;

            lock (runtimeAsyncTaskTimestamps)
            {
                runtimeAsyncTaskTimestamps.Remove(task.Id);
            }
        }

        internal static void RemoveRuntimeAsyncTask(Task task, Continuation continuationChain)
        {
            RemoveRuntimeAsyncTask(task);

            Dictionary<object, long>? continuationTimestamps = s_runtimeAsyncContinuationTimestamps;
            if (continuationTimestamps is null)
                return;

            lock (continuationTimestamps)
            {
                Continuation? nc = continuationChain;
                while (nc != null)
                {
                    continuationTimestamps.Remove(nc);
                    nc = nc.Next;
                }
            }
        }
#endif

        // We moved a number of Task properties into this class.  The idea is that in most cases, these properties never
        // need to be accessed during the life cycle of a Task, so we don't want to instantiate them every time.  Once
        // one of these properties needs to be written, we will instantiate a ContingentProperties object and set
        // the appropriate property.
        internal sealed class ContingentProperties
        {
            // Additional context

            internal ExecutionContext? m_capturedContext; // The execution context to run the task within, if any. Only set from non-concurrent contexts.

            // Completion fields (exceptions and event)

            internal volatile ManualResetEventSlim? m_completionEvent; // Lazily created if waiting is required.
            internal volatile TaskExceptionHolder? m_exceptionsHolder; // Tracks exceptions, if any have occurred

            // Cancellation fields (token, registration, and internally requested)

            internal CancellationToken m_cancellationToken; // Task's cancellation token, if it has one
            internal StrongBox<CancellationTokenRegistration>? m_cancellationRegistration; // Task's registration with the cancellation token
            internal volatile int m_internalCancellationRequested; // Its own field because multiple threads legally try to set it.

            // Parenting fields

            // # of active children + 1 (for this task itself).
            // Used for ensuring all children are done before this task can complete
            // The extra count helps prevent the race condition for executing the final state transition
            // (i.e. whether the last child or this task itself should call FinishStageTwo())
            internal volatile int m_completionCountdown = 1;
            // A list of child tasks that threw an exception (TCEs don't count),
            // but haven't yet been waited on by the parent, lazily initialized.
            internal volatile List<Task>? m_exceptionalChildren;
            // A task's parent, or null if parent-less. Only set during Task construction.
            internal Task? m_parent;

            /// <summary>
            /// Sets the internal completion event.
            /// </summary>
            internal void SetCompleted()
            {
                ManualResetEventSlim? mres = m_completionEvent;
                if (mres != null) SetEvent(mres);
            }

            internal static void SetEvent(ManualResetEventSlim mres)
            {
                try
                {
                    mres.Set();
                }
                catch (ObjectDisposedException)
                {
                    // The event may have been exposed to external code, in which case it could have been disposed
                    // prematurely / erroneously.  To avoid that causing problems for the unrelated stack trying to
                    // set the event, eat any disposed exceptions.
                }
            }

            /// <summary>
            /// Checks if we registered a CT callback during construction, and unregisters it.
            /// This should be called when we know the registration isn't useful anymore. Specifically from Finish() if the task has completed
            /// successfully or with an exception.
            /// </summary>
            internal void UnregisterCancellationCallback()
            {
                if (m_cancellationRegistration != null)
                {
                    // Harden against ODEs thrown from disposing of the CTR.
                    // Since the task has already been put into a final state by the time this
                    // is called, all we can do here is suppress the exception.
                    try { m_cancellationRegistration.Value.Dispose(); }
                    catch (ObjectDisposedException) { }
                    m_cancellationRegistration = null;
                }
            }
        }

        // This field will only be instantiated to some non-null value if any ContingentProperties need to be set.
        // This will be a ContingentProperties instance or a type derived from it
        internal ContingentProperties? m_contingentProperties;

        // Special internal constructor to create an already-completed task.
        // if canceled==true, create a Canceled task, or else create a RanToCompletion task.
        // Constructs the task as already completed
        internal Task(bool canceled, TaskCreationOptions creationOptions, CancellationToken ct)
        {
            _ = s_asyncDebuggingEnabled; // Debugger depends on Task cctor being run when any Task gets created
            int optionFlags = (int)creationOptions;
            if (canceled)
            {
                m_stateFlags = (int)TaskStateFlags.Canceled | (int)TaskStateFlags.CancellationAcknowledged | optionFlags;
                m_contingentProperties = new ContingentProperties() // can't have children, so just instantiate directly
                {
                    m_cancellationToken = ct,
                    m_internalCancellationRequested = CANCELLATION_REQUESTED,
                };
            }
            else
            {
                m_stateFlags = (int)TaskStateFlags.RanToCompletion | optionFlags;
            }
        }

        /// <summary>Constructor for use with promise-style tasks that aren't configurable.</summary>
        internal Task()
        {
            _ = s_asyncDebuggingEnabled; // Debugger depends on Task cctor being run when any Task gets created
            m_stateFlags = (int)TaskStateFlags.WaitingForActivation | (int)InternalTaskOptions.PromiseTask;
        }

        // Special constructor for use with promise-style tasks.
        // Added promiseStyle parameter as an aid to the compiler to distinguish between (state,TCO) and
        // (action,TCO).  It should always be true.
        internal Task(object? state, TaskCreationOptions creationOptions, bool promiseStyle)
        {
            _ = s_asyncDebuggingEnabled; // Debugger depends on Task cctor being run when any Task gets created

            // Check the creationOptions. We allow the AttachedToParent option to be specified for promise tasks.
            // Also allow RunContinuationsAsynchronously because this is the constructor called by TCS
            CheckPromiseCreationOptions(creationOptions);

            // Only set a parent if AttachedToParent is specified.
            if ((creationOptions & TaskCreationOptions.AttachedToParent) != 0)
            {
                Task? parent = InternalCurrent;
                if (parent != null)
                {
                    EnsureContingentPropertiesInitializedUnsafe().m_parent = parent;
                }
            }

            TaskConstructorCore(null, state, default, creationOptions, InternalTaskOptions.PromiseTask, null);
        }

        internal static void CheckPromiseCreationOptions(TaskCreationOptions creationOptions)
        {
            if ((creationOptions & ~(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously)) != 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.creationOptions);
            }
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the Task.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="action"/> argument is null.</exception>
        public Task(Action action)
            : this(action, null, null, default, TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action and <see cref="Threading.CancellationToken">CancellationToken</see>.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the Task.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new Task.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="action"/> argument is null.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Action action, CancellationToken cancellationToken)
            : this(action, null, null, cancellationToken, TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action and creation options.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the task.</param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the Task's behavior.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        public Task(Action action, TaskCreationOptions creationOptions)
            : this(action, null, InternalCurrentIfAttached(creationOptions), default, creationOptions, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action and creation options.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the task.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the Task's behavior.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Action action, CancellationToken cancellationToken, TaskCreationOptions creationOptions)
            : this(action, null, InternalCurrentIfAttached(creationOptions), cancellationToken, creationOptions, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action and state.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the task.</param>
        /// <param name="state">An object representing data to be used by the action.</param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> argument is null.
        /// </exception>
        public Task(Action<object?> action, object? state)
            : this(action, state, null, default, TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action, state, and options.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the task.</param>
        /// <param name="state">An object representing data to be used by the action.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Action<object?> action, object? state, CancellationToken cancellationToken)
            : this(action, state, null, cancellationToken, TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action, state, and options.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the task.</param>
        /// <param name="state">An object representing data to be used by the action.</param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the Task's behavior.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        public Task(Action<object?> action, object? state, TaskCreationOptions creationOptions)
            : this(action, state, InternalCurrentIfAttached(creationOptions), default, creationOptions, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task"/> with the specified action, state, and options.
        /// </summary>
        /// <param name="action">The delegate that represents the code to execute in the task.</param>
        /// <param name="state">An object representing data to be used by the action.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the Task's behavior.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Action<object?> action, object? state, CancellationToken cancellationToken, TaskCreationOptions creationOptions)
            : this(action, state, InternalCurrentIfAttached(creationOptions), cancellationToken, creationOptions, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// An internal constructor used by the factory methods on task and its descendent(s).
        /// </summary>
        /// <param name="action">An action to execute.</param>
        /// <param name="state">Optional state to pass to the action.</param>
        /// <param name="parent">Parent of Task.</param>
        /// <param name="cancellationToken">A CancellationToken for the task.</param>
        /// <param name="scheduler">A task scheduler under which the task will run.</param>
        /// <param name="creationOptions">Options to control its execution.</param>
        /// <param name="internalOptions">Internal options to control its execution</param>
        internal Task(Delegate action, object? state, Task? parent, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, InternalTaskOptions internalOptions, TaskScheduler? scheduler)
        {
            _ = s_asyncDebuggingEnabled; // Debugger depends on Task cctor being run when any Task gets created
            if (action == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.action);
            }

            // Keep a link to the parent if attached
            if (parent != null && (creationOptions & TaskCreationOptions.AttachedToParent) != 0)
            {
                EnsureContingentPropertiesInitializedUnsafe().m_parent = parent;
            }

            TaskConstructorCore(action, state, cancellationToken, creationOptions, internalOptions, scheduler);

            CapturedContext = ExecutionContext.Capture();
        }

        /// <summary>
        /// Common logic used by the following internal ctors:
        ///     Task()
        ///     Task(object action, object state, Task parent, TaskCreationOptions options, TaskScheduler taskScheduler)
        /// </summary>
        /// <param name="action">Action for task to execute.</param>
        /// <param name="state">Object to which to pass to action (may be null)</param>
        /// <param name="scheduler">Task scheduler on which to run thread (only used by continuation tasks).</param>
        /// <param name="cancellationToken">A CancellationToken for the Task.</param>
        /// <param name="creationOptions">Options to customize behavior of Task.</param>
        /// <param name="internalOptions">Internal options to customize behavior of Task.</param>
        internal void TaskConstructorCore(Delegate? action, object? state, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, InternalTaskOptions internalOptions, TaskScheduler? scheduler)
        {
            m_action = action;
            m_stateObject = state;
            m_taskScheduler = scheduler;

            // Check for validity of options
            if ((creationOptions &
                    ~(TaskCreationOptions.AttachedToParent |
                      TaskCreationOptions.LongRunning |
                      TaskCreationOptions.DenyChildAttach |
                      TaskCreationOptions.HideScheduler |
                      TaskCreationOptions.PreferFairness |
                      TaskCreationOptions.RunContinuationsAsynchronously)) != 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.creationOptions);
            }

            int tmpFlags = (int)creationOptions | (int)internalOptions; // one write to the volatile m_stateFlags instead of two when setting the above options
            m_stateFlags = m_action == null || (internalOptions & InternalTaskOptions.ContinuationTask) != 0 ?
                tmpFlags | (int)TaskStateFlags.WaitingForActivation :
                tmpFlags;

            // Now is the time to add the new task to the children list
            // of the creating task if the options call for it.
            // We can safely call the creator task's AddNewChild() method to register it,
            // because at this point we are already on its thread of execution.

            ContingentProperties? props = m_contingentProperties;
            if (props != null)
            {
                Task? parent = props.m_parent;
                if (parent != null
                    && ((creationOptions & TaskCreationOptions.AttachedToParent) != 0)
                    && ((parent.CreationOptions & TaskCreationOptions.DenyChildAttach) == 0))
                {
                    parent.AddNewChild();
                }
            }

            // if we have a non-null cancellationToken, allocate the contingent properties to save it
            // we need to do this as the very last thing in the construction path, because the CT registration could modify m_stateFlags
            if (cancellationToken.CanBeCanceled)
            {
                AssignCancellationToken(cancellationToken, null, null);
            }
        }

        /// <summary>
        /// Handles everything needed for associating a CancellationToken with a task which is being constructed.
        /// This method is meant to be called either from the TaskConstructorCore or from ContinueWithCore.
        /// </summary>
        private void AssignCancellationToken(CancellationToken cancellationToken, Task? antecedent, TaskContinuation? continuation)
        {
            // There is no need to worry about concurrency issues here because we are in the constructor path of the task --
            // there should not be any race conditions to set m_contingentProperties at this point.
            ContingentProperties props = EnsureContingentPropertiesInitializedUnsafe();
            props.m_cancellationToken = cancellationToken;

            try
            {
                // If an unstarted task has a valid CancellationToken that gets signalled while the task is still not queued
                // we need to proactively cancel it, because it may never execute to transition itself.
                // The only way to accomplish this is to register a callback on the CT.
                // We exclude Promise tasks from this, because TaskCompletionSource needs to fully control the inner tasks's lifetime (i.e. not allow external cancellations)
                if (((InternalTaskOptions)Options &
                    (InternalTaskOptions.QueuedByRuntime | InternalTaskOptions.PromiseTask | InternalTaskOptions.LazyCancellation)) == 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        // Fast path for an already-canceled cancellationToken
                        InternalCancel();
                    }
                    else
                    {
                        // Regular path for an uncanceled cancellationToken
                        CancellationTokenRegistration ctr;
                        if (antecedent == null)
                        {
                            // if no antecedent was specified, use this task's reference as the cancellation state object
                            ctr = cancellationToken.UnsafeRegister(static t => ((Task)t!).InternalCancel(), this);
                        }
                        else
                        {
                            // If an antecedent was specified, pack this task, its antecedent and the TaskContinuation together as a tuple
                            // and use it as the cancellation state object. This will be unpacked in the cancellation callback so that
                            // antecedent.RemoveCancellation(continuation) can be invoked.
                            ctr = cancellationToken.UnsafeRegister(static t =>
                            {
                                var tuple = (TupleSlim<Task, Task, TaskContinuation>)t!;

                                Task targetTask = tuple.Item1;
                                Task antecedentTask = tuple.Item2;

                                antecedentTask.RemoveContinuation(tuple.Item3);
                                targetTask.InternalCancel();
                            }, new TupleSlim<Task, Task, TaskContinuation>(this, antecedent, continuation));
                        }

                        props.m_cancellationRegistration = new StrongBox<CancellationTokenRegistration>(ctr);
                    }
                }
            }
            catch
            {
                // If we have an exception related to our CancellationToken, then we need to subtract ourselves
                // from our parent before throwing it.
                Task? parent = m_contingentProperties?.m_parent;
                if ((parent != null) &&
                    ((Options & TaskCreationOptions.AttachedToParent) != 0)
                     && ((parent.Options & TaskCreationOptions.DenyChildAttach) == 0))
                {
                    parent.DisregardChild();
                }
                throw;
            }
        }

        // Debugger support
        private string DebuggerDisplayMethodDescription => m_action?.Method.ToString() ?? "{null}";

        // Internal property to process TaskCreationOptions access and mutation.
        internal TaskCreationOptions Options => OptionsMethod(m_stateFlags);

        // Similar to Options property, but allows for the use of a cached flags value rather than
        // a read of the volatile m_stateFlags field.
        internal static TaskCreationOptions OptionsMethod(int flags)
        {
            return (TaskCreationOptions)(flags & (int)TaskStateFlags.OptionsMask);
        }

        // Atomically OR-in newBits to m_stateFlags, while making sure that
        // no illegalBits are set.  Returns true on success, false on failure.
        internal bool AtomicStateUpdate(int newBits, int illegalBits)
        {
            int oldFlags = m_stateFlags;
            return
                (oldFlags & illegalBits) == 0 &&
                (Interlocked.CompareExchange(ref m_stateFlags, oldFlags | newBits, oldFlags) == oldFlags ||
                 AtomicStateUpdateSlow(newBits, illegalBits));
        }

        private bool AtomicStateUpdateSlow(int newBits, int illegalBits)
        {
            int flags = m_stateFlags;
            while (true)
            {
                if ((flags & illegalBits) != 0) return false;
                int oldFlags = Interlocked.CompareExchange(ref m_stateFlags, flags | newBits, flags);
                if (oldFlags == flags)
                {
                    return true;
                }
                flags = oldFlags;
            }
        }

        internal bool AtomicStateUpdate(int newBits, int illegalBits, ref int oldFlags)
        {
            int flags = oldFlags = m_stateFlags;
            while (true)
            {
                if ((flags & illegalBits) != 0) return false;
                oldFlags = Interlocked.CompareExchange(ref m_stateFlags, flags | newBits, flags);
                if (oldFlags == flags)
                {
                    return true;
                }
                flags = oldFlags;
            }
        }

        /// <summary>
        /// Sets or clears the TaskStateFlags.WaitCompletionNotification state bit.
        /// The debugger sets this bit to aid it in "stepping out" of an async method body.
        /// If enabled is true, this must only be called on a task that has not yet been completed.
        /// If enabled is false, this may be called on completed tasks.
        /// Either way, it should only be used for promise-style tasks.
        /// </summary>
        /// <param name="enabled">true to set the bit; false to unset the bit.</param>
        internal void SetNotificationForWaitCompletion(bool enabled)
        {
            if (enabled)
            {
                // Atomically set the TaskStateFlags.WaitCompletionNotification bit
                bool success = AtomicStateUpdate((int)TaskStateFlags.WaitCompletionNotification,
                                  (int)TaskStateFlags.CompletedMask | (int)TaskStateFlags.CompletionReserved);
            }
            else
            {
                // Atomically clear the TaskStateFlags.WaitCompletionNotification bit
                Interlocked.And(ref m_stateFlags, ~(int)TaskStateFlags.WaitCompletionNotification);
            }
        }

        /// <summary>
        /// Calls the debugger notification method if the right bit is set and if
        /// the task itself allows for the notification to proceed.
        /// </summary>
        /// <returns>true if the debugger was notified; otherwise, false.</returns>
        internal bool NotifyDebuggerOfWaitCompletionIfNecessary()
        {
            // Notify the debugger if of any of the tasks we've waited on requires notification
            if (IsWaitNotificationEnabled && ShouldNotifyDebuggerOfWaitCompletion)
            {
                NotifyDebuggerOfWaitCompletion();
                return true;
            }
            return false;
        }

        /// <summary>Returns true if any of the supplied tasks require wait notification.</summary>
        /// <param name="tasks">The tasks to check.</param>
        /// <returns>true if any of the tasks require notification; otherwise, false.</returns>
        internal static bool AnyTaskRequiresNotifyDebuggerOfWaitCompletion(Task?[] tasks)
        {
            foreach (Task? task in tasks)
            {
                if (task != null &&
                    task.IsWaitNotificationEnabled &&
                    task.ShouldNotifyDebuggerOfWaitCompletion) // potential recursion
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Gets whether either the end await bit is set or (not xor) the task has not completed successfully.</summary>
        /// <returns>(DebuggerBitSet || !RanToCompletion)</returns>
        internal bool IsWaitNotificationEnabledOrNotRanToCompletion
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (m_stateFlags & ((int)TaskStateFlags.WaitCompletionNotification | (int)TaskStateFlags.RanToCompletion))
                        != (int)TaskStateFlags.RanToCompletion;
        }

        /// <summary>
        /// Determines whether we should inform the debugger that we're ending a join with a task.
        /// This should only be called if the debugger notification bit is set, as it is has some cost,
        /// namely it is a virtual call (however calling it if the bit is not set is not functionally
        /// harmful).  Derived implementations may choose to only conditionally call down to this base
        /// implementation.
        /// </summary>
        private protected virtual bool ShouldNotifyDebuggerOfWaitCompletion
        {
            get
            {
                // It's theoretically possible but extremely rare that this assert could fire because the
                // bit was unset between the time that it was checked and this method was called.
                // It's so remote a chance that it's worth having the assert to protect against misuse.
                bool isWaitNotificationEnabled = IsWaitNotificationEnabled;
                return isWaitNotificationEnabled;
            }
        }

        /// <summary>Gets whether the task's debugger notification for wait completion bit is set.</summary>
        /// <returns>true if the bit is set; false if it's not set.</returns>
        [FeatureSwitchDefinition("System.Diagnostics.Debugger.IsSupported")]
        internal bool IsWaitNotificationEnabled => // internal only to enable unit tests; would otherwise be private
            (m_stateFlags & (int)TaskStateFlags.WaitCompletionNotification) != 0;

        /// <summary>Placeholder method used as a breakpoint target by the debugger.  Must not be inlined or optimized.</summary>
        /// <remarks>All joins with a task should end up calling this if their debugger notification bit is set.</remarks>
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        private void NotifyDebuggerOfWaitCompletion()
        {
            // Now that we're notifying the debugger, clear the bit.  The debugger should do this anyway,
            // but this adds a bit of protection in case it fails to, and given that the debugger is involved,
            // the overhead here for the interlocked is negligable.  We do still rely on the debugger
            // to clear bits, as this doesn't recursively clear bits in the case of, for example, WhenAny.
            SetNotificationForWaitCompletion(enabled: false);
        }

        // Atomically mark a Task as started while making sure that it is not canceled.
        internal bool MarkStarted()
        {
            return AtomicStateUpdate((int)TaskStateFlags.Started, (int)TaskStateFlags.Canceled | (int)TaskStateFlags.Started);
        }

        internal void FireTaskScheduledIfNeeded(TaskScheduler ts)
        {
            if ((m_stateFlags & (int)TaskStateFlags.TaskScheduledWasFired) == 0)
            {
                m_stateFlags |= (int)TaskStateFlags.TaskScheduledWasFired;

                if (TplEventSource.Log.IsEnabled())
                {
                    Task? currentTask = InternalCurrent;
                    Task? parentTask = m_contingentProperties?.m_parent;
                    TplEventSource.Log.TaskScheduled(ts.Id, currentTask == null ? 0 : currentTask.Id,
                                        this.Id, parentTask == null ? 0 : parentTask.Id, (int)this.Options);
                }
            }
        }

        /// <summary>
        /// Internal function that will be called by a new child task to add itself to
        /// the children list of the parent (this).
        ///
        /// Since a child task can only be created from the thread executing the action delegate
        /// of this task, reentrancy is neither required nor supported. This should not be called from
        /// anywhere other than the task construction/initialization codepaths.
        /// </summary>
        internal void AddNewChild()
        {
            ContingentProperties props = EnsureContingentPropertiesInitialized();

            if (props.m_completionCountdown == 1)
            {
                // A count of 1 indicates so far there was only the parent, and this is the first child task
                // Single kid => no fuss about who else is accessing the count. Let's save ourselves 100 cycles
                props.m_completionCountdown++;
            }
            else
            {
                // otherwise do it safely
                Interlocked.Increment(ref props.m_completionCountdown);
            }
        }

        // This is called in the case where a new child is added, but then encounters a CancellationToken-related exception.
        // We need to subtract that child from m_completionCountdown, or the parent will never complete.
        internal void DisregardChild()
        {
            ContingentProperties props = EnsureContingentPropertiesInitialized();
            Interlocked.Decrement(ref props.m_completionCountdown);
        }

        /// <summary>
        /// Starts the <see cref="Task"/>, scheduling it for execution to the current <see
        /// cref="TaskScheduler">TaskScheduler</see>.
        /// </summary>
        /// <remarks>
        /// A task may only be started and run only once.  Any attempts to schedule a task a second time
        /// will result in an exception.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The <see cref="Task"/> is not in a valid state to be started. It may have already been started,
        /// executed, or canceled, or it may have been created in a manner that doesn't support direct
        /// scheduling.
        /// </exception>
        public void Start()
        {
            Start(TaskScheduler.Current);
        }

        /// <summary>
        /// Starts the <see cref="Task"/>, scheduling it for execution to the specified <see
        /// cref="TaskScheduler">TaskScheduler</see>.
        /// </summary>
        /// <remarks>
        /// A task may only be started and run only once. Any attempts to schedule a task a second time will
        /// result in an exception.
        /// </remarks>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler">TaskScheduler</see> with which to associate
        /// and execute this task.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The <see cref="Task"/> is not in a valid state to be started. It may have already been started,
        /// executed, or canceled, or it may have been created in a manner that doesn't support direct
        /// scheduling.
        /// </exception>
        public void Start(TaskScheduler scheduler)
        {
            // Read the volatile m_stateFlags field once and cache it for subsequent operations
            int flags = m_stateFlags;

            // Need to check this before (m_action == null) because completed tasks will
            // set m_action to null.  We would want to know if this is the reason that m_action == null.
            if (IsCompletedMethod(flags))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_Start_TaskCompleted);
            }

            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            TaskCreationOptions options = OptionsMethod(flags);
            if ((options & (TaskCreationOptions)InternalTaskOptions.PromiseTask) != 0)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_Start_Promise);
            }
            if ((options & (TaskCreationOptions)InternalTaskOptions.ContinuationTask) != 0)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_Start_ContinuationTask);
            }

            // Make sure that Task only gets started once.  Or else throw an exception.
            if (Interlocked.CompareExchange(ref m_taskScheduler, scheduler, null) != null)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_Start_AlreadyStarted);
            }

            ScheduleAndStart(true);
        }

        /// <summary>
        /// Runs the <see cref="Task"/> synchronously on the current <see
        /// cref="TaskScheduler">TaskScheduler</see>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A task may only be started and run only once. Any attempts to schedule a task a second time will
        /// result in an exception.
        /// </para>
        /// <para>
        /// Tasks executed with <see cref="RunSynchronously()"/> will be associated with the current <see
        /// cref="TaskScheduler">TaskScheduler</see>.
        /// </para>
        /// <para>
        /// If the target scheduler does not support running this Task on the current thread, the Task will
        /// be scheduled for execution on the scheduler, and the current thread will block until the
        /// Task has completed execution.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The <see cref="Task"/> is not in a valid state to be started. It may have already been started,
        /// executed, or canceled, or it may have been created in a manner that doesn't support direct
        /// scheduling.
        /// </exception>
        public void RunSynchronously()
        {
            InternalRunSynchronously(TaskScheduler.Current, waitForCompletion: true);
        }

        /// <summary>
        /// Runs the <see cref="Task"/> synchronously on the <see
        /// cref="TaskScheduler">scheduler</see> provided.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A task may only be started and run only once. Any attempts to schedule a task a second time will
        /// result in an exception.
        /// </para>
        /// <para>
        /// If the target scheduler does not support running this Task on the current thread, the Task will
        /// be scheduled for execution on the scheduler, and the current thread will block until the
        /// Task has completed execution.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The <see cref="Task"/> is not in a valid state to be started. It may have already been started,
        /// executed, or canceled, or it may have been created in a manner that doesn't support direct
        /// scheduling.
        /// </exception>
        /// <exception cref="ArgumentNullException">The <paramref name="scheduler"/> parameter
        /// is null.</exception>
        /// <param name="scheduler">The scheduler on which to attempt to run this task inline.</param>
        public void RunSynchronously(TaskScheduler scheduler)
        {
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            InternalRunSynchronously(scheduler, waitForCompletion: true);
        }

        //
        // Internal version of RunSynchronously that allows not waiting for completion.
        //
        internal void InternalRunSynchronously(TaskScheduler scheduler, bool waitForCompletion)
        {
            // Read the volatile m_stateFlags field once and cache it for subsequent operations
            int flags = m_stateFlags;

            // Can't call this method on a continuation task
            TaskCreationOptions options = OptionsMethod(flags);
            if ((options & (TaskCreationOptions)InternalTaskOptions.ContinuationTask) != 0)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_RunSynchronously_Continuation);
            }

            // Can't call this method on a promise-style task
            if ((options & (TaskCreationOptions)InternalTaskOptions.PromiseTask) != 0)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_RunSynchronously_Promise);
            }

            // Can't call this method on a task that has already completed
            if (IsCompletedMethod(flags))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_RunSynchronously_TaskCompleted);
            }

            // Make sure that Task only gets started once.  Or else throw an exception.
            if (Interlocked.CompareExchange(ref m_taskScheduler, scheduler, null) != null)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_RunSynchronously_AlreadyStarted);
            }

            // execute only if we successfully cancel when concurrent cancel attempts are made.
            // otherwise throw an exception, because we've been canceled.
            if (MarkStarted())
            {
                bool taskQueued = false;
                try
                {
                    // We wrap TryRunInline() in a try/catch block and move an excepted task to Faulted here,
                    // but not in Wait()/WaitAll()/FastWaitAll().  Here, we know for sure that the
                    // task will not be subsequently scheduled (assuming that the scheduler adheres
                    // to the guideline that an exception implies that no state change took place),
                    // so it is safe to catch the exception and move the task to a final state.  The
                    // same cannot be said for Wait()/WaitAll()/FastWaitAll().
                    taskQueued = scheduler.TryRunInline(this, false);
                    if (!taskQueued)
                    {
                        scheduler.InternalQueueTask(this);
                        taskQueued = true; // only mark this after successfully queuing the task.
                    }

                    // A successful TryRunInline doesn't guarantee completion, as there may be unfinished children.
                    // Also if we queued the task above, the task may not be done yet.
                    if (waitForCompletion && !IsCompleted)
                    {
                        SpinThenBlockingWait(Timeout.Infinite, default);
                    }
                }
                catch (Exception e)
                {
                    // we received an unexpected exception originating from a custom scheduler, which needs to be wrapped in a TSE and thrown
                    if (!taskQueued)
                    {
                        // We had a problem with TryRunInline() or QueueTask().
                        // Record the exception, marking ourselves as Completed/Faulted.
                        TaskSchedulerException tse = new TaskSchedulerException(e);
                        AddException(tse);
                        Finish(false);

                        m_contingentProperties.m_exceptionsHolder.MarkAsHandled(false);

                        // And re-throw.
                        throw tse;
                    }
                    // We had a problem with waiting. Just re-throw.
                    else throw;
                }
            }
            else
            {
                // Can't call this method on canceled task.
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_RunSynchronously_TaskCompleted);
            }
        }

        ////
        //// Helper methods for Factory StartNew methods.
        ////

        // Implicitly converts action to object and handles the meat of the StartNew() logic.
        internal static Task InternalStartNew(
            Task? creatingTask, Delegate action, object? state, CancellationToken cancellationToken, TaskScheduler scheduler,
            TaskCreationOptions options, InternalTaskOptions internalOptions)
        {
            // Validate arguments.
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            // Create and schedule the task. This throws an InvalidOperationException if already shut down.
            // Here we add the InternalTaskOptions.QueuedByRuntime to the internalOptions, so that TaskConstructorCore can skip the cancellation token registration
            Task t = new Task(action, state, creatingTask, cancellationToken, options, internalOptions | InternalTaskOptions.QueuedByRuntime, scheduler);

            t.ScheduleAndStart(false);
            return t;
        }

        /// <summary>
        /// Gets a unique ID for a <see cref="Task">Task</see> or task continuation instance.
        /// </summary>
        internal static int NewId()
        {
            int newId;

            // We need to repeat if Interlocked.Increment wraps around and returns 0.
            // Otherwise next time this task's Id is queried it will get a new value
            do
            {
                newId = Interlocked.Increment(ref s_taskIdCounter);
            }
            while (newId == 0);

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.NewID(newId);

            return newId;
        }

        /////////////
        // properties

        /// <summary>
        /// Gets a unique ID for this <see cref="Task">Task</see> instance.
        /// </summary>
        /// <remarks>
        /// Task IDs are assigned on-demand and do not necessarily represent the order in the which Task
        /// instances were created.
        /// </remarks>
        public int Id
        {
            get
            {
                if (Volatile.Read(ref m_taskId) == 0)
                {
                    int newId = NewId();
                    Interlocked.CompareExchange(ref m_taskId, newId, 0);
                }

                return m_taskId;
            }
        }

        /// <summary>
        /// Returns the unique ID of the currently executing <see cref="Task">Task</see>.
        /// </summary>
        public static int? CurrentId
        {
            get
            {
                Task? currentTask = InternalCurrent;
                if (currentTask != null)
                    return currentTask.Id;
                else
                    return null;
            }
        }

        /// <summary>
        /// Gets the <see cref="Task">Task</see> instance currently executing, or
        /// null if none exists.
        /// </summary>
        internal static Task? InternalCurrent => t_currentTask;

        /// <summary>
        /// Gets the Task instance currently executing if the specified creation options
        /// contain AttachedToParent.
        /// </summary>
        /// <param name="creationOptions">The options to check.</param>
        /// <returns>The current task if there is one and if AttachToParent is in the options; otherwise, null.</returns>
        internal static Task? InternalCurrentIfAttached(TaskCreationOptions creationOptions)
        {
            return (creationOptions & TaskCreationOptions.AttachedToParent) != 0 ? InternalCurrent : null;
        }

        /// <summary>
        /// Gets the <see cref="AggregateException">Exception</see> that caused the <see
        /// cref="Task">Task</see> to end prematurely. If the <see
        /// cref="Task">Task</see> completed successfully or has not yet thrown any
        /// exceptions, this will return null.
        /// </summary>
        /// <remarks>
        /// Tasks that throw unhandled exceptions store the resulting exception and propagate it wrapped in a
        /// <see cref="AggregateException"/> in calls to <see cref="Wait()">Wait</see>
        /// or in accesses to the <see cref="Exception"/> property.  Any exceptions not observed by the time
        /// the Task instance is garbage collected will be propagated on the finalizer thread.
        /// </remarks>
        public AggregateException? Exception
        {
            get
            {
                AggregateException? e = null;

                // If you're faulted, retrieve the exception(s)
                if (IsFaulted) e = GetExceptions(false);

                return e;
            }
        }

        /// <summary>
        /// Gets the <see cref="TaskStatus">TaskStatus</see> of this Task.
        /// </summary>
        public TaskStatus Status
        {
            get
            {
                TaskStatus rval;

                // get a cached copy of the state flags.  This should help us
                // to get a consistent view of the flags if they are changing during the
                // execution of this method.
                int sf = m_stateFlags;

                if ((sf & (int)TaskStateFlags.Faulted) != 0) rval = TaskStatus.Faulted;
                else if ((sf & (int)TaskStateFlags.Canceled) != 0) rval = TaskStatus.Canceled;
                else if ((sf & (int)TaskStateFlags.RanToCompletion) != 0) rval = TaskStatus.RanToCompletion;
                else if ((sf & (int)TaskStateFlags.WaitingOnChildren) != 0) rval = TaskStatus.WaitingForChildrenToComplete;
                else if ((sf & (int)TaskStateFlags.DelegateInvoked) != 0) rval = TaskStatus.Running;
                else if ((sf & (int)TaskStateFlags.Started) != 0) rval = TaskStatus.WaitingToRun;
                else if ((sf & (int)TaskStateFlags.WaitingForActivation) != 0) rval = TaskStatus.WaitingForActivation;
                else rval = TaskStatus.Created;

                return rval;
            }
        }

        /// <summary>
        /// Gets whether this <see cref="Task">Task</see> instance has completed
        /// execution due to being canceled.
        /// </summary>
        /// <remarks>
        /// A <see cref="Task">Task</see> will complete in Canceled state either if its <see cref="CancellationToken">CancellationToken</see>
        /// was marked for cancellation before the task started executing, or if the task acknowledged the cancellation request on
        /// its already signaled CancellationToken by throwing an
        /// <see cref="OperationCanceledException">OperationCanceledException</see> that bears the same
        /// <see cref="Threading.CancellationToken">CancellationToken</see>.
        /// </remarks>
        public bool IsCanceled =>
                // Return true if canceled bit is set and faulted bit is not set
                (m_stateFlags & ((int)TaskStateFlags.Canceled | (int)TaskStateFlags.Faulted)) == (int)TaskStateFlags.Canceled;

        /// <summary>
        /// Returns true if this task has a cancellation token and it was signaled.
        /// To be used internally in execute entry codepaths.
        /// </summary>
        internal bool IsCancellationRequested
        {
            get
            {
                // check both the internal cancellation request flag and the CancellationToken attached to this task
                ContingentProperties? props = Volatile.Read(ref m_contingentProperties);
                return props != null &&
                    (props.m_internalCancellationRequested == CANCELLATION_REQUESTED ||
                     props.m_cancellationToken.IsCancellationRequested);
            }
        }

        /// <summary>
        /// Ensures that the contingent properties field has been initialized.
        /// ASSUMES THAT m_stateFlags IS ALREADY SET!
        /// </summary>
        /// <returns>The initialized contingent properties object.</returns>
        internal ContingentProperties EnsureContingentPropertiesInitialized()
        {
            return Volatile.Read(ref m_contingentProperties) ?? InitializeContingentProperties();

            ContingentProperties InitializeContingentProperties()
            {
                Interlocked.CompareExchange(ref m_contingentProperties, new ContingentProperties(), null);
                return m_contingentProperties;
            }
        }

        /// <summary>
        /// Without synchronization, ensures that the contingent properties field has been initialized.
        /// ASSUMES THAT m_stateFlags IS ALREADY SET!
        /// </summary>
        /// <returns>The initialized contingent properties object.</returns>
        internal ContingentProperties EnsureContingentPropertiesInitializedUnsafe() =>
            m_contingentProperties ??= new ContingentProperties();

        /// <summary>
        /// This internal property provides access to the CancellationToken that was set on the task
        /// when it was constructed.
        /// </summary>
        internal CancellationToken CancellationToken
        {
            get
            {
                ContingentProperties? props = Volatile.Read(ref m_contingentProperties);
                return (props == null) ? default : props.m_cancellationToken;
            }
        }

        /// <summary>
        /// Gets whether this <see cref="Task"/> threw an OperationCanceledException while its CancellationToken was signaled.
        /// </summary>
        internal bool IsCancellationAcknowledged => (m_stateFlags & (int)TaskStateFlags.CancellationAcknowledged) != 0;

        /// <summary>
        /// Gets whether this <see cref="Task">Task</see> has completed.
        /// </summary>
        /// <remarks>
        /// <see cref="IsCompleted"/> will return true when the Task is in one of the three
        /// final states: <see cref="TaskStatus.RanToCompletion">RanToCompletion</see>,
        /// <see cref="TaskStatus.Faulted">Faulted</see>, or
        /// <see cref="TaskStatus.Canceled">Canceled</see>.
        /// </remarks>
        public bool IsCompleted
        {
            get
            {
                int stateFlags = m_stateFlags; // enable inlining of IsCompletedMethod by "cast"ing away the volatility
                return IsCompletedMethod(stateFlags);
            }
        }

        // Similar to IsCompleted property, but allows for the use of a cached flags value
        // rather than reading the volatile m_stateFlags field.
        private static bool IsCompletedMethod(int flags)
        {
            return (flags & (int)TaskStateFlags.CompletedMask) != 0;
        }

        public bool IsCompletedSuccessfully => (m_stateFlags & (int)TaskStateFlags.CompletedMask) == (int)TaskStateFlags.RanToCompletion;

        /// <summary>
        /// Gets the <see cref="TaskCreationOptions">TaskCreationOptions</see> used
        /// to create this task.
        /// </summary>
        public TaskCreationOptions CreationOptions => Options & (TaskCreationOptions)(~InternalTaskOptions.InternalOptionsMask);

        /// <summary>Spins until the task is completed.</summary>
        /// <remarks>This should only be called if the task is in the process of being completed by another thread.</remarks>
        internal void SpinUntilCompleted()
        {
            // Spin wait until the completion is finalized by another thread.
            SpinWait sw = default;
            while (!IsCompleted)
            {
                sw.SpinOnce();
            }
        }

        /// <summary>
        /// Gets a <see cref="WaitHandle"/> that can be used to wait for the task to
        /// complete.
        /// </summary>
        /// <remarks>
        /// Using the wait functionality provided by <see cref="Wait()"/>
        /// should be preferred over using <see cref="IAsyncResult.AsyncWaitHandle"/> for similar
        /// functionality.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The <see cref="Task"/> has been disposed.
        /// </exception>
        WaitHandle IAsyncResult.AsyncWaitHandle
        {
            // Although a slim event is used internally to avoid kernel resource allocation, this function
            // forces allocation of a true WaitHandle when called.
            get
            {
                bool isDisposed = (m_stateFlags & (int)TaskStateFlags.Disposed) != 0;
                if (isDisposed)
                {
                    ThrowHelper.ThrowObjectDisposedException(ExceptionResource.Task_ThrowIfDisposed);
                }
                return CompletedEvent.WaitHandle;
            }
        }

        /// <summary>
        /// Gets the state object supplied when the <see cref="Task">Task</see> was created,
        /// or null if none was supplied.
        /// </summary>
        public object? AsyncState => (m_stateFlags & (int)InternalTaskOptions.HiddenState) == 0 ? m_stateObject : null;

        /// <summary>
        /// Gets an indication of whether the asynchronous operation completed synchronously.
        /// </summary>
        /// <value>true if the asynchronous operation completed synchronously; otherwise, false.</value>
        bool IAsyncResult.CompletedSynchronously => false;

        /// <summary>
        /// Provides access to the TaskScheduler responsible for executing this Task.
        /// </summary>
        internal TaskScheduler? ExecutingTaskScheduler => m_taskScheduler;

        /// <summary>
        /// Provides access to factory methods for creating <see cref="Task"/> and <see cref="Task{TResult}"/> instances.
        /// </summary>
        /// <remarks>
        /// The factory returned from <see cref="Factory"/> is a default instance
        /// of <see cref="TaskFactory"/>, as would result from using
        /// the default constructor on TaskFactory.
        /// </remarks>
        public static TaskFactory Factory { get; } = new TaskFactory();

        /// <summary>Singleton cached task that's been completed successfully.</summary>
        /// <remarks>It's a <see cref="Task{VoidTaskResult}"/> so it can be shared with <see cref="AsyncTaskMethodBuilder"/>.</remarks>
        internal static readonly Task<VoidTaskResult> s_cachedCompleted = new Task<VoidTaskResult>(false, default, (TaskCreationOptions)InternalTaskOptions.DoNotDispose, default);

        /// <summary>Gets a task that's already been completed successfully.</summary>
        public static Task CompletedTask
        {
            [Intrinsic]
            get => s_cachedCompleted;
        }

        /// <summary>
        /// Provides an event that can be used to wait for completion.
        /// Only called by IAsyncResult.AsyncWaitHandle, which means that we really do need to instantiate a completion event.
        /// </summary>
        internal ManualResetEventSlim CompletedEvent
        {
            get
            {
                ContingentProperties contingentProps = EnsureContingentPropertiesInitialized();
                if (contingentProps.m_completionEvent == null)
                {
                    bool wasCompleted = IsCompleted;
                    ManualResetEventSlim newMre = new ManualResetEventSlim(wasCompleted);
                    if (Interlocked.CompareExchange(ref contingentProps.m_completionEvent, newMre, null) != null)
                    {
                        // Someone else already set the value, so we will just close the event right away.
                        newMre.Dispose();
                    }
                    else if (!wasCompleted && IsCompleted)
                    {
                        // We published the event as unset, but the task has subsequently completed.
                        // Set the event's state properly so that callers don't deadlock.
                        ContingentProperties.SetEvent(newMre);
                    }
                }

                return contingentProps.m_completionEvent;
            }
        }

        /// <summary>
        /// Whether an exception has been stored into the task.
        /// </summary>
        internal bool ExceptionRecorded
        {
            get
            {
                ContingentProperties? props = Volatile.Read(ref m_contingentProperties);
                return (props != null) && (props.m_exceptionsHolder != null) && (props.m_exceptionsHolder.ContainsFaultList);
            }
        }

        /// <summary>
        /// Gets whether the <see cref="Task"/> completed due to an unhandled exception.
        /// </summary>
        /// <remarks>
        /// If <see cref="IsFaulted"/> is true, the Task's <see cref="Status"/> will be equal to
        /// <see cref="TaskStatus.Faulted">TaskStatus.Faulted</see>, and its
        /// <see cref="Exception"/> property will be non-null.
        /// </remarks>
        [MemberNotNullWhen(true, nameof(Exception))]
        public bool IsFaulted =>
            // Faulted is "king" -- if that bit is present (regardless of other bits), we are faulted.
            (m_stateFlags & (int)TaskStateFlags.Faulted) != 0;

        /// <summary>
        /// The captured execution context for the current task to run inside
        /// If the TaskStateFlags.ExecutionContextIsNull flag is set, this means ExecutionContext.Capture returned null, otherwise
        /// If the captured context is the default, nothing is saved, otherwise the m_contingentProperties inflates to save the context
        /// </summary>
        internal ExecutionContext? CapturedContext
        {
            get
            {
                if ((m_stateFlags & (int)TaskStateFlags.ExecutionContextIsNull) == (int)TaskStateFlags.ExecutionContextIsNull)
                {
                    return null;
                }
                else
                {
                    return m_contingentProperties?.m_capturedContext ?? ExecutionContext.Default;
                }
            }
            set
            {
                // There is no need to atomically set this bit because this set() method is only called during construction, and therefore there should be no contending accesses to m_stateFlags
                if (value == null)
                {
                    m_stateFlags |= (int)TaskStateFlags.ExecutionContextIsNull;
                }
                else if (value != ExecutionContext.Default) // not the default context, then inflate the contingent properties and set it
                {
                    EnsureContingentPropertiesInitializedUnsafe().m_capturedContext = value;
                }
                // else do nothing, this is the default context
            }
        }

        /////////////
        // methods

        /// <summary>
        /// Disposes the <see cref="Task"/>, releasing all of its unmanaged resources.
        /// </summary>
        /// <remarks>
        /// Unlike most of the members of <see cref="Task"/>, this method is not thread-safe.
        /// Also, <see cref="Dispose()"/> may only be called on a <see cref="Task"/> that is in one of
        /// the final states: <see cref="TaskStatus.RanToCompletion">RanToCompletion</see>,
        /// <see cref="TaskStatus.Faulted">Faulted</see>, or
        /// <see cref="TaskStatus.Canceled">Canceled</see>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The exception that is thrown if the <see cref="Task"/> is not in
        /// one of the final states: <see cref="TaskStatus.RanToCompletion">RanToCompletion</see>,
        /// <see cref="TaskStatus.Faulted">Faulted</see>, or
        /// <see cref="TaskStatus.Canceled">Canceled</see>.
        /// </exception>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the <see cref="Task"/>, releasing all of its unmanaged resources.
        /// </summary>
        /// <param name="disposing">
        /// A Boolean value that indicates whether this method is being called due to a call to <see
        /// cref="Dispose()"/>.
        /// </param>
        /// <remarks>
        /// Unlike most of the members of <see cref="Task"/>, this method is not thread-safe.
        /// </remarks>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose is a nop if this task was created with the DoNotDispose internal option.
                // This is done before the completed check, because if we're not touching any
                // state on the task, it's ok for it to happen before completion.
                if ((Options & (TaskCreationOptions)InternalTaskOptions.DoNotDispose) != 0)
                {
                    return;
                }

                // Task must be completed to dispose
                if (!IsCompleted)
                {
                    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.Task_Dispose_NotCompleted);
                }

                // Dispose of the underlying completion event if it exists
                ContingentProperties? cp = Volatile.Read(ref m_contingentProperties);
                if (cp != null)
                {
                    // Make a copy to protect against racing Disposes.
                    // If we wanted to make this a bit safer, we could use an interlocked here,
                    // but we state that Dispose is not thread safe.
                    ManualResetEventSlim? ev = cp.m_completionEvent;
                    if (ev != null)
                    {
                        // Null out the completion event in contingent props; we'll use our copy from here on out
                        cp.m_completionEvent = null;

                        // In the unlikely event that our completion event is inflated but not yet signaled,
                        // go ahead and signal the event.  If you dispose of an unsignaled MRES, then any waiters
                        // will deadlock; an ensuing Set() will not wake them up.  In the event of an AppDomainUnload,
                        // there is no guarantee that anyone else is going to signal the event, and it does no harm to
                        // call Set() twice on m_completionEvent.
                        ContingentProperties.SetEvent(ev);

                        // Finally, dispose of the event
                        ev.Dispose();
                    }
                }
            }

            // We OR the flags to indicate the object has been disposed. The task
            // has already completed at this point, and the only conceivable race condition would
            // be with the unsetting of the TaskStateFlags.WaitCompletionNotification flag, which
            // is extremely unlikely and also benign.  (Worst case: we hit a breakpoint
            // twice instead of once in the debugger.  Weird, but not lethal.)
            m_stateFlags |= (int)TaskStateFlags.Disposed;
        }

        /////////////
        // internal helpers

        /// <summary>
        /// Schedules the task for execution.
        /// </summary>
        /// <param name="needsProtection">If true, TaskStateFlags.Started bit is turned on in
        /// an atomic fashion, making sure that TaskStateFlags.Canceled does not get set
        /// underneath us.  If false, TaskStateFlags.Started bit is OR-ed right in.  This
        /// allows us to streamline things a bit for StartNew(), where competing cancellations
        /// are not a problem.</param>
        internal void ScheduleAndStart(bool needsProtection)
        {
            // Set the TaskStateFlags.Started bit
            if (needsProtection)
            {
                if (!MarkStarted())
                {
                    // A cancel has snuck in before we could get started.  Quietly exit.
                    return;
                }
            }
            else
            {
                m_stateFlags |= (int)TaskStateFlags.Started;
            }

            if (s_asyncDebuggingEnabled)
                AddToActiveTasks(this);

            if (TplEventSource.Log.IsEnabled() &&
                (Options & (TaskCreationOptions)InternalTaskOptions.ContinuationTask) == 0 &&
                m_action is Delegate action)
            {
                // For all other task than TaskContinuations we want to log. TaskContinuations log in their constructor
                TplEventSource.Log.TraceOperationBegin(this.Id, "Task: " + action.GetMethodName(), 0);
            }

            try
            {
                // Queue to the indicated scheduler.
                m_taskScheduler.InternalQueueTask(this);
            }
            catch (Exception e)
            {
                // The scheduler had a problem queueing this task.  Record the exception, leaving this task in
                // a Faulted state.
                TaskSchedulerException tse = new TaskSchedulerException(e);
                AddException(tse);
                Finish(false);

                // Now we need to mark ourselves as "handled" to avoid crashing the finalizer thread if we are called from StartNew(),
                // because the exception is either propagated outside directly, or added to an enclosing parent. However we won't do this for
                // continuation tasks, because in that case we internally eat the exception and therefore we need to make sure the user does
                // later observe it explicitly or see it on the finalizer.

                if ((Options & (TaskCreationOptions)InternalTaskOptions.ContinuationTask) == 0)
                {
                    m_contingentProperties.m_exceptionsHolder.MarkAsHandled(false);
                }
                // re-throw the exception wrapped as a TaskSchedulerException.
                throw tse;
            }
        }

        /// <summary>
        /// Adds an exception to the list of exceptions this task has thrown.
        /// </summary>
        /// <param name="exceptionObject">An object representing either an Exception or a collection of Exceptions.</param>
        internal void AddException(object exceptionObject)
        {
            AddException(exceptionObject, representsCancellation: false);
        }

        /// <summary>
        /// Adds an exception to the list of exceptions this task has thrown.
        /// </summary>
        /// <param name="exceptionObject">An object representing either an Exception or a collection of Exceptions.</param>
        /// <param name="representsCancellation">Whether the exceptionObject is an OperationCanceledException representing cancellation.</param>
        internal void AddException(object exceptionObject, bool representsCancellation)
        {
            //
            // WARNING: A great deal of care went into ensuring that
            // AddException() and GetExceptions() are never called
            // simultaneously.  See comment at start of GetExceptions().
            //

            // Lazily initialize the holder, ensuring only one thread wins.
            ContingentProperties props = EnsureContingentPropertiesInitialized();
            if (props.m_exceptionsHolder == null)
            {
                TaskExceptionHolder holder = new TaskExceptionHolder(this);
                if (Interlocked.CompareExchange(ref props.m_exceptionsHolder, holder, null) != null)
                {
                    // If someone else already set the value, suppress finalization.
                    holder.MarkAsHandled(false);
                }
            }

            lock (props)
            {
                props.m_exceptionsHolder.Add(exceptionObject, representsCancellation);
            }
        }

        /// <summary>
        /// Returns a list of exceptions by aggregating the holder's contents. Or null if
        /// no exceptions have been thrown.
        /// </summary>
        /// <param name="includeTaskCanceledExceptions">Whether to include a TCE if cancelled.</param>
        /// <returns>An aggregate exception, or null if no exceptions have been caught.</returns>
        private AggregateException? GetExceptions(bool includeTaskCanceledExceptions)
        {
            //
            // WARNING: The Task/Task<TResult>/TaskCompletionSource classes
            // have all been carefully crafted to ensure that GetExceptions()
            // is never called while AddException() is being called.  There
            // are locks taken on m_contingentProperties in several places:
            //
            // -- Task<TResult>.TrySetException(): The lock allows the
            //    task to be set to Faulted state, and all exceptions to
            //    be recorded, in one atomic action.
            //
            // -- Task.Exception_get(): The lock ensures that Task<TResult>.TrySetException()
            //    is allowed to complete its operation before Task.Exception_get()
            //    can access GetExceptions().
            //
            // -- Task.ThrowIfExceptional(): The lock ensures that Wait() will
            //    not attempt to call GetExceptions() while Task<TResult>.TrySetException()
            //    is in the process of calling AddException().
            //
            // For "regular" tasks, we effectively keep AddException() and GetException()
            // from being called concurrently by the way that the state flows.  Until
            // a Task is marked Faulted, Task.Exception_get() returns null.  And
            // a Task is not marked Faulted until it and all of its children have
            // completed, which means that all exceptions have been recorded.
            //
            // It might be a lot easier to follow all of this if we just required
            // that all calls to GetExceptions() and AddExceptions() were made
            // under a lock on m_contingentProperties.  But that would also
            // increase our lock occupancy time and the frequency with which we
            // would need to take the lock.
            //
            // If you add a call to GetExceptions() anywhere in the code,
            // please continue to maintain the invariant that it can't be
            // called when AddException() is being called.
            //

            // We'll lazily create a TCE if the task has been canceled.
            Exception? canceledException = null;
            if (includeTaskCanceledExceptions && IsCanceled)
            {
                // Backcompat:
                // Ideally we'd just use the cached OCE from this.GetCancellationExceptionDispatchInfo()
                // here.  However, that would result in a potentially breaking change from .NET 4, which
                // has the code here that throws a new exception instead of the original, and the EDI
                // may not contain a TCE, but an OCE or any OCE-derived type, which would mean we'd be
                // propagating an exception of a different type.
                canceledException = new TaskCanceledException(this);
                canceledException.SetCurrentStackTrace();
            }

            if (ExceptionRecorded)
            {
                // No need to lock around this, as other logic prevents the consumption of exceptions
                // before they have been completely processed.
                return m_contingentProperties.m_exceptionsHolder.CreateExceptionObject(false, canceledException);
            }
            else if (canceledException != null)
            {
                // No exceptions, but there was a cancelation. Aggregate and return it.
                return new AggregateException(canceledException);
            }

            return null;
        }

        /// <summary>Gets the exception dispatch infos once the task has faulted.</summary>
        internal List<ExceptionDispatchInfo> GetExceptionDispatchInfos()
        {
            return m_contingentProperties!.m_exceptionsHolder!.GetExceptionDispatchInfos();
        }

        /// <summary>Gets the ExceptionDispatchInfo containing the OperationCanceledException for this task.</summary>
        /// <returns>The ExceptionDispatchInfo.  May be null if no OCE was stored for the task.</returns>
        internal ExceptionDispatchInfo? GetCancellationExceptionDispatchInfo()
        {
            return Volatile.Read(ref m_contingentProperties)?.m_exceptionsHolder?.GetCancellationExceptionDispatchInfo(); // may be null
        }

        /// <summary>Marks any exceptions stored in the Task as having been handled.</summary>
        internal void MarkExceptionsAsHandled()
        {
            Volatile.Read(ref m_contingentProperties)?.m_exceptionsHolder?.MarkAsHandled(calledFromFinalizer: false);
        }

        /// <summary>
        /// Throws an aggregate exception if the task contains exceptions.
        /// </summary>
        internal void ThrowIfExceptional(bool includeTaskCanceledExceptions)
        {
            Exception? exception = GetExceptions(includeTaskCanceledExceptions);
            if (exception != null)
            {
                UpdateExceptionObservedStatus();
                throw exception;
            }
        }

        /// <summary>Throws the exception on the ThreadPool.</summary>
        /// <param name="exception">The exception to propagate.</param>
        /// <param name="targetContext">The target context on which to propagate the exception.  Null to use the ThreadPool.</param>
        internal static void ThrowAsync(Exception exception, SynchronizationContext? targetContext)
        {
            // Capture the exception into an ExceptionDispatchInfo so that its
            // stack trace and Watson bucket info will be preserved
            var edi = ExceptionDispatchInfo.Capture(exception);

            // If the user supplied a SynchronizationContext...
            if (targetContext != null)
            {
                try
                {
                    // Post the throwing of the exception to that context, and return.
                    targetContext.Post(static state => ((ExceptionDispatchInfo)state!).Throw(), edi);
                    return;
                }
                catch (Exception postException)
                {
                    // If something goes horribly wrong in the Post, we'll
                    // propagate both exceptions on the ThreadPool
                    edi = ExceptionDispatchInfo.Capture(new AggregateException(exception, postException));
                }
            }

#if NATIVEAOT
            RuntimeExceptionHelpers.ReportUnhandledException(edi.SourceException);
#else
            // Propagate the exception(s) on the ThreadPool
            ThreadPool.QueueUserWorkItem(static state => ((ExceptionDispatchInfo)state!).Throw(), edi);

#endif // NATIVEAOT
        }

        /// <summary>
        /// Checks whether this is an attached task, and whether we are being called by the parent task.
        /// And sets the TaskStateFlags.ExceptionObservedByParent status flag based on that.
        ///
        /// This is meant to be used internally when throwing an exception, and when WaitAll is gathering
        /// exceptions for tasks it waited on. If this flag gets set, the implicit wait on children
        /// will skip exceptions to prevent duplication.
        ///
        /// This should only be called when this task has completed with an exception
        ///
        /// </summary>
        internal void UpdateExceptionObservedStatus()
        {
            Task? parent = m_contingentProperties?.m_parent;
            if ((parent != null)
                && ((Options & TaskCreationOptions.AttachedToParent) != 0)
                && ((parent.CreationOptions & TaskCreationOptions.DenyChildAttach) == 0)
                && InternalCurrent == parent)
            {
                m_stateFlags |= (int)TaskStateFlags.ExceptionObservedByParent;
            }
        }

        /// <summary>
        /// Checks whether the TaskStateFlags.ExceptionObservedByParent status flag is set,
        /// This will only be used by the implicit wait to prevent double throws
        ///
        /// </summary>
        internal bool IsExceptionObservedByParent => (m_stateFlags & (int)TaskStateFlags.ExceptionObservedByParent) != 0;

        /// <summary>
        /// Checks whether the body was ever invoked. Used by task scheduler code to verify custom schedulers actually ran the task.
        /// </summary>
        internal bool IsDelegateInvoked => (m_stateFlags & (int)TaskStateFlags.DelegateInvoked) != 0;

        /// <summary>
        /// Signals completion of this particular task.
        ///
        /// The userDelegateExecute parameter indicates whether this Finish() call comes following the
        /// full execution of the user delegate.
        ///
        /// If userDelegateExecute is false, it mean user delegate wasn't invoked at all (either due to
        /// a cancellation request, or because this task is a promise style Task). In this case, the steps
        /// involving child tasks (i.e. WaitForChildren) will be skipped.
        ///
        /// </summary>
        internal void Finish(bool userDelegateExecute)
        {
            if (m_contingentProperties == null)
            {
                FinishStageTwo();
            }
            else
            {
                FinishSlow(userDelegateExecute);
            }
        }

        private void FinishSlow(bool userDelegateExecute)
        {
            if (!userDelegateExecute)
            {
                // delegate didn't execute => no children. We can safely call the remaining finish stages
                FinishStageTwo();
            }
            else
            {
                ContingentProperties props = m_contingentProperties!;

                // Count of 1 => either all children finished, or there were none. Safe to complete ourselves
                // without paying the price of an Interlocked.Decrement.
                if ((props.m_completionCountdown == 1) ||
                    Interlocked.Decrement(ref props.m_completionCountdown) == 0) // Reaching this sub clause means there may be remaining active children,
                                                                                 // and we could be racing with one of them to call FinishStageTwo().
                                                                                 // So whoever does the final Interlocked.Dec is responsible to finish.
                {
                    FinishStageTwo();
                }
                else
                {
                    // Apparently some children still remain. It will be up to the last one to process the completion of this task on their own thread.
                    // We will now yield the thread back to ThreadPool. Mark our state appropriately before getting out.

                    // We have to use an atomic update for this and make sure not to overwrite a final state,
                    // because at this very moment the last child's thread may be concurrently completing us.
                    // Otherwise we risk overwriting the TaskStateFlags.RanToCompletion, _CANCELED or _FAULTED bit which may have been set by that child task.
                    // Note that the concurrent update by the last child happening in FinishStageTwo could still wipe out the TaskStateFlags.WaitingOnChildren flag,
                    // but it is not critical to maintain, therefore we dont' need to intruduce a full atomic update into FinishStageTwo

                    AtomicStateUpdate((int)TaskStateFlags.WaitingOnChildren, (int)TaskStateFlags.Faulted | (int)TaskStateFlags.Canceled | (int)TaskStateFlags.RanToCompletion);
                }

                // Now is the time to prune exceptional children. We'll walk the list and removes the ones whose exceptions we might have observed after they threw.
                // we use a local variable for exceptional children here because some other thread may be nulling out m_contingentProperties.m_exceptionalChildren
                List<Task>? exceptionalChildren = props.m_exceptionalChildren;
                if (exceptionalChildren != null)
                {
                    lock (exceptionalChildren)
                    {
                        exceptionalChildren.RemoveAll(t => t.IsExceptionObservedByParent); // RemoveAll has better performance than doing it ourselves
                    }
                }
            }
        }

        /// <summary>
        /// FinishStageTwo is to be executed as soon as we known there are no more children to complete.
        /// It can happen i) either on the thread that originally executed this task (if no children were spawned, or they all completed by the time this task's delegate quit)
        ///              ii) or on the thread that executed the last child.
        /// </summary>
        private void FinishStageTwo()
        {
            // At this point, the task is done executing and waiting for its children,
            // we can transition our task to a completion state.

            ContingentProperties? cp = Volatile.Read(ref m_contingentProperties);
            if (cp != null)
            {
                AddExceptionsFromChildren(cp);
            }

            int completionState;
            if (ExceptionRecorded)
            {
                completionState = (int)TaskStateFlags.Faulted;
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Error);

                if (s_asyncDebuggingEnabled)
                    RemoveFromActiveTasks(this);
            }
            else if (IsCancellationRequested && IsCancellationAcknowledged)
            {
                // We transition into the TaskStateFlags.Canceled final state if the task's CT was signalled for cancellation,
                // and the user delegate acknowledged the cancellation request by throwing an OCE,
                // and the task hasn't otherwise transitioned into faulted state. (TaskStateFlags.Faulted trumps TaskStateFlags.Canceled)
                //
                // If the task threw an OCE without cancellation being requestsed (while the CT not being in signaled state),
                // then we regard it as a regular exception

                completionState = (int)TaskStateFlags.Canceled;
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Canceled);

                if (s_asyncDebuggingEnabled)
                    RemoveFromActiveTasks(this);
            }
            else
            {
                completionState = (int)TaskStateFlags.RanToCompletion;
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);

                if (s_asyncDebuggingEnabled)
                    RemoveFromActiveTasks(this);
            }

            // Use Interlocked.Exchange() to effect a memory fence, preventing
            // any SetCompleted() (or later) instructions from sneak back before it.
            Interlocked.Exchange(ref m_stateFlags, m_stateFlags | completionState);

            // Set the completion event if it's been lazy allocated.
            // And if we made a cancellation registration, it's now unnecessary.
            cp = Volatile.Read(ref m_contingentProperties); // need to re-read after updating state
            if (cp != null)
            {
                cp.SetCompleted();
                cp.UnregisterCancellationCallback();
            }

            // ready to run continuations and notify parent.
            FinishStageThree();
        }

        /// <summary>
        /// Final stage of the task completion code path. Notifies the parent (if any) that another of its children are done, and runs continuations.
        /// This function is only separated out from FinishStageTwo because these two operations are also needed to be called from CancellationCleanupLogic()
        /// </summary>
        internal void FinishStageThree()
        {
            // Release the action so that holding this task object alive doesn't also
            // hold alive the body of the task.  We do this before notifying a parent,
            // so that if notifying the parent completes the parent and causes
            // its synchronous continuations to run, the GC can collect the state
            // in the interim.  And we do it before finishing continuations, because
            // continuations hold onto the task, and therefore are keeping it alive.
            m_action = null;

            ContingentProperties? cp = m_contingentProperties;
            if (cp != null)
            {
                // Similarly, null out any ExecutionContext we may have captured,
                // to avoid keeping state like async locals alive unnecessarily
                // when the Task is kept alive.
                cp.m_capturedContext = null;

                // Notify parent if this was an attached task
                NotifyParentIfPotentiallyAttachedTask();
            }

            // Activate continuations (if any).
            FinishContinuations();
        }

        internal void NotifyParentIfPotentiallyAttachedTask()
        {
            Task? parent = m_contingentProperties?.m_parent;
            if (parent != null
                 && ((parent.CreationOptions & TaskCreationOptions.DenyChildAttach) == 0)
                 && (((TaskCreationOptions)(m_stateFlags & (int)TaskStateFlags.OptionsMask)) & TaskCreationOptions.AttachedToParent) != 0)
            {
                parent.ProcessChildCompletion(this);
            }
        }

        /// <summary>
        /// This is called by children of this task when they are completed.
        /// </summary>
        internal void ProcessChildCompletion(Task childTask)
        {
            ContingentProperties? props = Volatile.Read(ref m_contingentProperties);

            // if the child threw and we haven't observed it we need to save it for future reference
            if (childTask.IsFaulted && !childTask.IsExceptionObservedByParent)
            {
                // Lazily initialize the child exception list
                if (props!.m_exceptionalChildren == null)
                {
                    Interlocked.CompareExchange(ref props.m_exceptionalChildren, new List<Task>(), null);
                }

                // In rare situations involving AppDomainUnload, it's possible (though unlikely) for FinishStageTwo() to be called
                // multiple times for the same task.  In that case, AddExceptionsFromChildren() could be nulling m_exceptionalChildren
                // out at the same time that we're processing it, resulting in a NullReferenceException here.  We'll protect
                // ourselves by caching m_exceptionChildren in a local variable.
                List<Task>? tmp = props.m_exceptionalChildren;
                if (tmp != null)
                {
                    lock (tmp)
                    {
                        tmp.Add(childTask);
                    }
                }
            }

            if (Interlocked.Decrement(ref props!.m_completionCountdown) == 0)
            {
                // This call came from the final child to complete, and apparently we have previously given up this task's right to complete itself.
                // So we need to invoke the final finish stage.

                FinishStageTwo();
            }
        }

        /// <summary>
        /// This is to be called just before the task does its final state transition.
        /// It traverses the list of exceptional children, and appends their aggregate exceptions into this one's exception list
        /// </summary>
        internal void AddExceptionsFromChildren(ContingentProperties props)
        {
            // In rare occurrences during AppDomainUnload() processing, it is possible for this method to be called
            // simultaneously on the same task from two different contexts.  This can result in m_exceptionalChildren
            // being nulled out while it is being processed, which could lead to a NullReferenceException.  To
            // protect ourselves, we'll cache m_exceptionalChildren in a local variable.
            List<Task>? exceptionalChildren = props.m_exceptionalChildren;

            if (exceptionalChildren != null)
            {
                // This lock is necessary because even though AddExceptionsFromChildren is last to execute, it may still
                // be racing with the code segment at the bottom of Finish() that prunes the exceptional child array.
                lock (exceptionalChildren)
                {
                    foreach (Task task in exceptionalChildren)
                    {
                        if (task.IsFaulted && !task.IsExceptionObservedByParent)
                        {
                            TaskExceptionHolder? exceptionHolder = Volatile.Read(ref task.m_contingentProperties)!.m_exceptionsHolder;

                            // No locking necessary since child task is finished adding exceptions
                            // and concurrent CreateExceptionObject() calls do not constitute
                            // a concurrency hazard.
                            AddException(exceptionHolder.CreateExceptionObject(false, null));
                        }
                    }
                }

                // Reduce memory pressure by getting rid of the array
                props.m_exceptionalChildren = null;
            }
        }

        /// <summary>
        /// Outermost entry function to execute this task. Handles all aspects of executing a task on the caller thread.
        /// </summary>
        internal bool ExecuteEntry()
        {
            // Do atomic state transition from queued to invoked. If we observe a task that's already invoked,
            // we will return false so that TaskScheduler.ExecuteTask can throw an exception back to the custom scheduler.
            // However we don't want this exception to be throw if the task was already canceled, because it's a
            // legitimate scenario for custom schedulers to dequeue a task and mark it as canceled (example: throttling scheduler)
            int previousState = 0;
            if (!AtomicStateUpdate((int)TaskStateFlags.DelegateInvoked,
                                    (int)TaskStateFlags.DelegateInvoked | (int)TaskStateFlags.CompletedMask,
                                    ref previousState) && (previousState & (int)TaskStateFlags.Canceled) == 0)
            {
                // This task has already been invoked.  Don't invoke it again.
                return false;
            }

            if (!IsCancellationRequested & !IsCanceled)
            {
                ExecuteWithThreadLocal(ref t_currentTask);
            }
            else
            {
                ExecuteEntryCancellationRequestedOrCanceled();
            }

            return true;
        }

        /// <summary>
        /// This is used internally to execute the Task directly. ThreadPool uses this,
        /// and it is also used to invoke runtime async tasks directly.
        /// The base behavior is simply to use the entry point that's not protected from
        /// double-invoke; derived internal tasks can override to customize their behavior,
        /// which is usually done by promises that want to reuse the same object as a queued work item.
        /// </summary>
        internal virtual void ExecuteDirectly(Thread? threadPoolThread) => ExecuteEntryUnsafe(threadPoolThread);

        internal void ExecuteEntryUnsafe(Thread? threadPoolThread) // used instead of ExecuteEntry() when we don't have to worry about double-execution prevent
        {
            // Remember that we started running the task delegate.
            m_stateFlags |= (int)TaskStateFlags.DelegateInvoked;

            if (!IsCancellationRequested & !IsCanceled)
            {
                ExecuteWithThreadLocal(ref t_currentTask, threadPoolThread);
            }
            else
            {
                ExecuteEntryCancellationRequestedOrCanceled();
            }
        }

        internal void ExecuteEntryCancellationRequestedOrCanceled()
        {
            if (!IsCanceled)
            {
                int prevState = Interlocked.Exchange(ref m_stateFlags, m_stateFlags | (int)TaskStateFlags.Canceled);
                if ((prevState & (int)TaskStateFlags.Canceled) == 0)
                {
                    CancellationCleanupLogic();
                }
            }
        }

        // A trick so we can refer to the TLS slot with a byref.
        private void ExecuteWithThreadLocal(ref Task? currentTaskSlot, Thread? threadPoolThread = null)
        {
            // Remember the current task so we can restore it after running, and then
            Task? previousTask = currentTaskSlot;

            // ETW event for Task Started
            TplEventSource log = TplEventSource.Log;
            Guid savedActivityID = default;
            bool etwIsEnabled = log.IsEnabled();
            if (etwIsEnabled)
            {
                if (log.TasksSetActivityIds)
                    EventSource.SetCurrentThreadActivityId(TplEventSource.CreateGuidForTaskID(this.Id), out savedActivityID);
                // previousTask holds the actual "current task" we want to report in the event
                if (previousTask != null)
                    log.TaskStarted(previousTask.m_taskScheduler!.Id, previousTask.Id, this.Id);
                else
                    log.TaskStarted(TaskScheduler.Current.Id, 0, this.Id);

                log.TraceSynchronousWorkBegin(this.Id, CausalitySynchronousWork.Execution);
            }

            try
            {
                // place the current task into TLS.
                currentTaskSlot = this;

                // Execute the task body
                try
                {
                    ExecutionContext? ec = CapturedContext;
                    if (ec == null)
                    {
                        // No context, just run the task directly.
                        InnerInvoke();
                    }
                    else
                    {
                        // Invoke it under the captured ExecutionContext
                        if (threadPoolThread is null)
                        {
                            ExecutionContext.RunInternal(ec, s_ecCallback, this);
                        }
                        else
                        {
                            ExecutionContext.RunFromThreadPoolDispatchLoop(threadPoolThread, ec, s_ecCallback, this);
                        }
                    }
                }
                catch (Exception exn)
                {
                    // Record this exception in the task's exception list
                    HandleException(exn);
                }

                if (etwIsEnabled)
                    log.TraceSynchronousWorkEnd(CausalitySynchronousWork.Execution);

                Finish(true);
            }
            finally
            {
                currentTaskSlot = previousTask;

                // ETW event for Task Completed
                if (etwIsEnabled)
                {
                    // previousTask holds the actual "current task" we want to report in the event
                    if (previousTask != null)
                        log.TaskCompleted(previousTask.m_taskScheduler!.Id, previousTask.Id, this.Id, IsFaulted);
                    else
                        log.TaskCompleted(TaskScheduler.Current.Id, 0, this.Id, IsFaulted);

                    if (log.TasksSetActivityIds)
                        EventSource.SetCurrentThreadActivityId(savedActivityID);
                }
            }
        }

        private static readonly ContextCallback s_ecCallback = obj =>
        {
            // Only used privately to pass directly to EC.Run
            Unsafe.As<Task>(obj).InnerInvoke();
        };

        /// <summary>
        /// The actual code which invokes the body of the task. This can be overridden in derived types.
        /// </summary>
        internal virtual void InnerInvoke()
        {
            if (m_action is Action action)
            {
                action();
                return;
            }

            if (m_action is Action<object?> actionWithState)
            {
                actionWithState(m_stateObject);
                return;
            }
        }

        /// <summary>
        /// Performs whatever handling is necessary for an unhandled exception. Normally
        /// this just entails adding the exception to the holder object.
        /// </summary>
        /// <param name="unhandledException">The exception that went unhandled.</param>
        private void HandleException(Exception unhandledException)
        {
            if (unhandledException is OperationCanceledException exceptionAsOce && IsCancellationRequested &&
                m_contingentProperties!.m_cancellationToken == exceptionAsOce.CancellationToken)
            {
                // All conditions are satisfied for us to go into canceled state in Finish().
                // Mark the acknowledgement.  The exception is also stored to enable it to be
                // the exception propagated from an await.

                SetCancellationAcknowledged();
                AddException(exceptionAsOce, representsCancellation: true);
            }
            else
            {
                // Other exceptions, including any OCE from the task that doesn't match the tasks' own CT,
                // or that gets thrown without the CT being set will be treated as an ordinary exception
                // and added to the aggregate.

                AddException(unhandledException);
            }
        }

        #region Await Support
        /// <summary>Gets an awaiter used to await this <see cref="Task"/>.</summary>
        /// <returns>An awaiter instance.</returns>
        public TaskAwaiter GetAwaiter()
        {
            return new TaskAwaiter(this);
        }

        /// <summary>Configures an awaiter used to await this <see cref="Task"/>.</summary>
        /// <param name="continueOnCapturedContext">
        /// true to attempt to marshal the continuation back to the original context captured; otherwise, false.
        /// </param>
        /// <returns>An object used to await this task.</returns>
        [Intrinsic]
        public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredTaskAwaitable(this, continueOnCapturedContext ? ConfigureAwaitOptions.ContinueOnCapturedContext : ConfigureAwaitOptions.None);
        }

        /// <summary>Configures an awaiter used to await this <see cref="Task"/>.</summary>
        /// <param name="options">Options used to configure how awaits on this task are performed.</param>
        /// <returns>An object used to await this task.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The <paramref name="options"/> argument specifies an invalid value.</exception>
        [Intrinsic]
        public ConfiguredTaskAwaitable ConfigureAwait(ConfigureAwaitOptions options)
        {
            if ((options & ~(ConfigureAwaitOptions.ContinueOnCapturedContext |
                             ConfigureAwaitOptions.SuppressThrowing |
                             ConfigureAwaitOptions.ForceYielding)) != 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.options);
            }

            return new ConfiguredTaskAwaitable(this, options);
        }

        /// <summary>
        /// Sets a continuation onto the <see cref="Task"/>.
        /// The continuation is scheduled to run in the current synchronization context is one exists,
        /// otherwise in the current task scheduler.
        /// </summary>
        /// <param name="continuationAction">The action to invoke when the <see cref="Task"/> has completed.</param>
        /// <param name="continueOnCapturedContext">
        /// true to attempt to marshal the continuation back to the original context captured; otherwise, false.
        /// </param>
        /// <param name="flowExecutionContext">Whether to flow ExecutionContext across the await.</param>
        /// <exception cref="InvalidOperationException">The awaiter was not properly initialized.</exception>
        internal void SetContinuationForAwait(
            Action continuationAction, bool continueOnCapturedContext, bool flowExecutionContext)
        {
            // Create the best AwaitTaskContinuation object given the request.
            // If this remains null by the end of the function, we can use the
            // continuationAction directly without wrapping it.
            TaskContinuation? tc;

            // If the user wants the continuation to run on the current "context" if there is one...
            if (continueOnCapturedContext)
            {
                // First try getting the current synchronization context.
                // If the current context is really just the base SynchronizationContext type,
                // which is intended to be equivalent to not having a current SynchronizationContext at all,
                // then ignore it.  This helps with performance by avoiding unnecessary posts and queueing
                // of work items, but more so it ensures that if code happens to publish the default context
                // as current, it won't prevent usage of a current task scheduler if there is one.
                if (SynchronizationContext.Current is SynchronizationContext syncCtx && syncCtx.GetType() != typeof(SynchronizationContext))
                {
                    tc = new SynchronizationContextAwaitTaskContinuation(syncCtx, continuationAction, flowExecutionContext);
                    goto HaveTaskContinuation;
                }

                // If there was no SynchronizationContext, then try for the current scheduler.
                // We only care about it if it's not the default.
                if (TaskScheduler.InternalCurrent is TaskScheduler scheduler && scheduler != TaskScheduler.Default)
                {
                    tc = new TaskSchedulerAwaitTaskContinuation(scheduler, continuationAction, flowExecutionContext);
                    goto HaveTaskContinuation;
                }
            }

            if (flowExecutionContext)
            {
                // We're targeting the default scheduler, so we can use the faster path
                // that assumes the default, and thus we don't need to store it.  If we're flowing
                // ExecutionContext, we need to capture it and wrap it in an AwaitTaskContinuation.
                // Otherwise, we're targeting the default scheduler and we don't need to flow ExecutionContext, so
                // we don't actually need a continuation object.  We can just store/queue the action itself.
                tc = new AwaitTaskContinuation(continuationAction, flowExecutionContext: true);
                goto HaveTaskContinuation;
            }

            if (!AddTaskContinuation(continuationAction, addBeforeOthers: false))
            {
                AwaitTaskContinuation.UnsafeScheduleAction(continuationAction, this);
            }
            return;

        HaveTaskContinuation:
            if (!AddTaskContinuation(tc, addBeforeOthers: false))
            {
                tc.Run(this, canInlineContinuationTask: false);
            }
        }

        /// <summary>
        /// Sets a continuation onto the <see cref="Task"/>.
        /// The continuation is scheduled to run in the current synchronization context is one exists,
        /// otherwise in the current task scheduler.
        /// </summary>
        /// <param name="stateMachineBox">The action to invoke when the <see cref="Task"/> has completed.</param>
        /// <param name="continueOnCapturedContext">
        /// true to attempt to marshal the continuation back to the original context captured; otherwise, false.
        /// </param>
        /// <exception cref="InvalidOperationException">The awaiter was not properly initialized.</exception>
        internal void UnsafeSetContinuationForAwait(IAsyncStateMachineBox stateMachineBox, bool continueOnCapturedContext)
        {
            // This code path doesn't emit all expected TPL-related events, such as for continuations.
            // It's expected that all callers check whether events are enabled before calling this function,
            // and only call it if they're not. However, as events can be dynamically turned
            // on and off, it's possible this assert could fire even when used correctly. Do not call this
            // method unless you've checked that TplEventSource.Log.IsEnabled() is false (if it becomes true after
            // the check, that's fine, and a natural race condition that's unavoidable).

            // Create the best AwaitTaskContinuation object given the request.
            // If this remains null by the end of the function, we can use the
            // continuationAction directly without wrapping it.
            TaskContinuation? tc;

            // If the caller wants to continue on the current context/scheduler and there is one,
            // fall back to using the state machine's delegate.
            if (continueOnCapturedContext)
            {
                if (SynchronizationContext.Current is SynchronizationContext syncCtx && syncCtx.GetType() != typeof(SynchronizationContext))
                {
                    tc = new SynchronizationContextAwaitTaskContinuation(syncCtx, stateMachineBox.MoveNextAction, flowExecutionContext: false);
                    goto HaveTaskContinuation;
                }

                if (TaskScheduler.InternalCurrent is TaskScheduler scheduler && scheduler != TaskScheduler.Default)
                {
                    tc = new TaskSchedulerAwaitTaskContinuation(scheduler, stateMachineBox.MoveNextAction, flowExecutionContext: false);
                    goto HaveTaskContinuation;
                }
            }

            // Otherwise, add the state machine box directly as the continuation.
            // If we're unable to because the task has already completed, queue it.
            if (!AddTaskContinuation(stateMachineBox, addBeforeOthers: false))
            {
                ThreadPool.UnsafeQueueUserWorkItemInternal(stateMachineBox, preferLocal: true);
            }
            return;

        HaveTaskContinuation:
            if (!AddTaskContinuation(tc, addBeforeOthers: false))
            {
                tc.Run(this, canInlineContinuationTask: false);
            }
        }

        /// <summary>Creates an awaitable that asynchronously yields back to the current context when awaited.</summary>
        /// <returns>
        /// A context that, when awaited, will asynchronously transition back into the current context at the
        /// time of the await. If the current SynchronizationContext is non-null, that is treated as the current context.
        /// Otherwise, TaskScheduler.Current is treated as the current context.
        /// </returns>
        public static YieldAwaitable Yield()
        {
            return default;
        }
        #endregion

        /// <summary>
        /// Waits for the <see cref="Task"/> to complete execution.
        /// </summary>
        /// <exception cref="AggregateException">
        /// The <see cref="Task"/> was canceled -or- an exception was thrown during
        /// the execution of the <see cref="Task"/>.
        /// </exception>
        public void Wait()
        {
            Wait(Timeout.Infinite, default);
        }

        /// <summary>
        /// Waits for the <see cref="Task"/> to complete execution.
        /// </summary>
        /// <param name="timeout">
        /// A <see cref="TimeSpan"/> that represents the number of milliseconds to wait, or a <see
        /// cref="TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <returns>
        /// true if the <see cref="Task"/> completed execution within the allotted time; otherwise, false.
        /// </returns>
        /// <exception cref="AggregateException">
        /// The <see cref="Task"/> was canceled -or- an exception was thrown during the execution of the <see
        /// cref="Task"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is a negative number other than -1 milliseconds, which represents an
        /// infinite time-out -or- timeout is greater than
        /// <see cref="int.MaxValue"/>.
        /// </exception>
        public bool Wait(TimeSpan timeout) => Wait(timeout, default);

        /// <summary>
        /// Waits for the <see cref="Task"/> to complete execution.
        /// </summary>
        /// <param name="timeout">The time to wait, or <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely</param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete.
        /// </param>
        /// <returns>
        /// true if the <see cref="Task"/> completed execution within the allotted time; otherwise, false.
        /// </returns>
        /// <exception cref="AggregateException">
        /// The <see cref="Task"/> was canceled -or- an exception was thrown during the execution of the <see
        /// cref="Task"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is a negative number other than -1 milliseconds, which represents an
        /// infinite time-out -or- timeout is greater than
        /// <see cref="int.MaxValue"/>.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.timeout);
            }
            cancellationToken.ThrowIfCancellationRequested();

            return Wait((int)totalMilliseconds, cancellationToken);
        }

        /// <summary>
        /// Waits for the <see cref="Task"/> to complete execution.
        /// </summary>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete.
        /// </param>
        /// <exception cref="OperationCanceledException">
        /// The <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        /// <exception cref="AggregateException">
        /// The <see cref="Task"/> was canceled -or- an exception was thrown during the execution of the <see
        /// cref="Task"/>.
        /// </exception>
        public void Wait(CancellationToken cancellationToken)
        {
            Wait(Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// Waits for the <see cref="Task"/> to complete execution.
        /// </summary>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or <see cref="Timeout.Infinite"/> (-1) to
        /// wait indefinitely.</param>
        /// <returns>true if the <see cref="Task"/> completed execution within the allotted time; otherwise,
        /// false.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="millisecondsTimeout"/> is a negative number other than -1, which represents an
        /// infinite time-out.
        /// </exception>
        /// <exception cref="AggregateException">
        /// The <see cref="Task"/> was canceled -or- an exception was thrown during the execution of the <see
        /// cref="Task"/>.
        /// </exception>
        public bool Wait(int millisecondsTimeout)
        {
            return Wait(millisecondsTimeout, default);
        }

        /// <summary>
        /// Waits for the <see cref="Task"/> to complete execution.
        /// </summary>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or <see cref="Timeout.Infinite"/> (-1) to
        /// wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete.
        /// </param>
        /// <returns>
        /// true if the <see cref="Task"/> completed execution within the allotted time; otherwise, false.
        /// </returns>
        /// <exception cref="AggregateException">
        /// The <see cref="Task"/> was canceled -or- an exception was thrown during the execution of the <see
        /// cref="Task"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="millisecondsTimeout"/> is a negative number other than -1, which represents an
        /// infinite time-out.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (millisecondsTimeout < -1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.millisecondsTimeout);
            }

            // Return immediately if we know that we've completed "clean" -- no exceptions, no cancellations
            // and if no notification to the debugger is required
            if (!IsWaitNotificationEnabledOrNotRanToCompletion) // (!DebuggerBitSet && RanToCompletion)
                return true;

            // Wait, and then return if we're still not done.
            if (!InternalWait(millisecondsTimeout, cancellationToken))
                return false;

            if (IsWaitNotificationEnabledOrNotRanToCompletion) // avoid a few unnecessary volatile reads if we completed successfully
            {
                // Notify the debugger of the wait completion if it's requested such a notification
                NotifyDebuggerOfWaitCompletionIfNecessary();

                // If cancellation was requested and the task was canceled, throw an
                // OperationCanceledException.  This is prioritized ahead of the ThrowIfExceptional
                // call to bring more determinism to cases where the same token is used to
                // cancel the Wait and to cancel the Task.  Otherwise, there's a race condition between
                // whether the Wait or the Task observes the cancellation request first,
                // and different exceptions result from the different cases.
                if (IsCanceled) cancellationToken.ThrowIfCancellationRequested();

                // If an exception occurred, or the task was cancelled, throw an exception.
                ThrowIfExceptional(true);
            }

            return true;
        }

        /// <summary>Gets a <see cref="Task"/> that will complete when this <see cref="Task"/> completes or when the specified <see cref="CancellationToken"/> has cancellation requested.</summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public Task WaitAsync(CancellationToken cancellationToken) => WaitAsync(Timeout.UnsignedInfinite, TimeProvider.System, cancellationToken);

        /// <summary>Gets a <see cref="Task"/> that will complete when this <see cref="Task"/> completes or when the specified timeout expires.</summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <returns>The <see cref="Task"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public Task WaitAsync(TimeSpan timeout) => WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), TimeProvider.System, default);

        /// <summary>Gets a <see cref="Task"/> that will complete when this <see cref="Task"/> completes or when the specified timeout expires.</summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret <paramref name="timeout"/>.</param>
        /// <returns>The <see cref="Task"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="timeProvider"/> argument is null.</exception>
        public Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            return WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), timeProvider, default);
        }

        /// <summary>Gets a <see cref="Task"/> that will complete when this <see cref="Task"/> completes, when the specified timeout expires, or when the specified <see cref="CancellationToken"/> has cancellation requested.</summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), TimeProvider.System, cancellationToken);

        /// <summary>Gets a <see cref="Task"/> that will complete when this <see cref="Task"/> completes, when the specified timeout expires, or when the specified <see cref="CancellationToken"/> has cancellation requested.</summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret <paramref name="timeout"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="timeProvider"/> argument is null.</exception>
        public Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            return WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), timeProvider, cancellationToken);
        }

        private Task WaitAsync(uint millisecondsTimeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            if (IsCompleted || (!cancellationToken.CanBeCanceled && millisecondsTimeout == Timeout.UnsignedInfinite))
            {
                return this;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            if (millisecondsTimeout == 0)
            {
                return FromException(new TimeoutException());
            }

            return new CancellationPromise<VoidTaskResult>(this, millisecondsTimeout, timeProvider, cancellationToken);
        }

        /// <summary>Task that's completed when another task, timeout, or cancellation token triggers.</summary>
        private protected sealed class CancellationPromise<TResult> : Task<TResult>, ITaskCompletionAction
        {
            /// <summary>The source task.  It's stored so that we can remove the continuation from it upon timeout or cancellation.</summary>
            private readonly Task _task;
            /// <summary>Cancellation registration used to unregister from the token source upon timeout or the task completing.</summary>
            private readonly CancellationTokenRegistration _registration;
            /// <summary>The timer used to implement the timeout.  It's stored so that it's rooted and so that we can dispose it upon cancellation or the task completing.</summary>
            private readonly ITimer? _timer;

            internal CancellationPromise(Task source, uint millisecondsDelay, TimeProvider timeProvider, CancellationToken token)
            {
                // Register with the target task.
                _task = source;
                source.AddCompletionAction(this);

                // Register with a timer if it's needed.
                if (millisecondsDelay != Timeout.UnsignedInfinite)
                {
                    TimerCallback callback = static state =>
                    {
                        var thisRef = (CancellationPromise<TResult>)state!;
                        if (thisRef.TrySetException(new TimeoutException()))
                        {
                            thisRef.Cleanup();
                        }
                    };

                    if (timeProvider == TimeProvider.System)
                    {
                        _timer = new TimerQueueTimer(callback, this, millisecondsDelay, Timeout.UnsignedInfinite, flowExecutionContext: false);
                    }
                    else
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            _timer = timeProvider.CreateTimer(callback, this, TimeSpan.FromMilliseconds(millisecondsDelay), Timeout.InfiniteTimeSpan);
                        }
                    }
                }

                // Register with the cancellation token.
                _registration = token.UnsafeRegister(static (state, cancellationToken) =>
                {
                    var thisRef = (CancellationPromise<TResult>)state!;
                    if (thisRef.TrySetCanceled(cancellationToken))
                    {
                        thisRef.Cleanup();
                    }
                }, this);

                // If one of the callbacks fired, it's possible they did so prior to our having registered the other callbacks,
                // and thus cleanup may have missed those additional registrations.  Just in case, check here, and if we're
                // already completed, unregister everything again.  Unregistration is idempotent and thread-safe.
                if (IsCompleted)
                {
                    Cleanup();
                }
            }

            bool ITaskCompletionAction.InvokeMayRunArbitraryCode => true;

            void ITaskCompletionAction.Invoke(Task completingTask)
            {
                bool set = completingTask.Status switch
                {
                    TaskStatus.Canceled => TrySetCanceled(completingTask.CancellationToken, completingTask.GetCancellationExceptionDispatchInfo()),
                    TaskStatus.Faulted => TrySetException(completingTask.GetExceptionDispatchInfos()),
                    _ => completingTask is Task<TResult> taskTResult ? TrySetResult(taskTResult.Result) : TrySetResult(),
                };

                if (set)
                {
                    Cleanup();
                }
            }

            private void Cleanup()
            {
                _registration.Dispose();
                _timer?.Dispose();
                _task.RemoveContinuation(this);
            }
        }

        // Convenience method that wraps any scheduler exception in a TaskSchedulerException
        // and rethrows it.
        private bool WrappedTryRunInline()
        {
            if (m_taskScheduler == null)
                return false;

            try
            {
                return m_taskScheduler.TryRunInline(this, true);
            }
            catch (Exception e)
            {
                throw new TaskSchedulerException(e);
            }
        }

        /// <summary>
        /// The core wait function, which is only accessible internally. It's meant to be used in places in TPL code where
        /// the current context is known or cached.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        internal bool InternalWait(int millisecondsTimeout, CancellationToken cancellationToken) =>
            InternalWaitCore(millisecondsTimeout, cancellationToken);

        // Separated out to allow it to be optimized (caller is marked NoOptimization for VS parallel debugger
        // to be able to see the method on the stack and inspect arguments).
        private bool InternalWaitCore(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            // If the task has already completed, there's nothing to wait for.
            bool returnValue = IsCompleted;
            if (returnValue)
            {
                return true;
            }

            // ETW event for Task Wait Begin
            TplEventSource log = TplEventSource.Log;
            bool etwIsEnabled = log.IsEnabled();
            if (etwIsEnabled)
            {
                Task? currentTask = InternalCurrent;
                log.TaskWaitBegin(
                    currentTask != null ? currentTask.m_taskScheduler!.Id : TaskScheduler.Default.Id, currentTask != null ? currentTask.Id : 0,
                    this.Id, TplEventSource.TaskWaitBehavior.Synchronous, 0);
            }

            // Alert a listening debugger that we can't make forward progress unless it slips threads.
            // We call NOCTD for two reasons:
            //    1. If the task runs on another thread, then we'll be blocked here indefinitely.
            //    2. If the task runs inline but takes some time to complete, it will suffer ThreadAbort with possible state corruption,
            //       and it is best to prevent this unless the user explicitly asks to view the value with thread-slipping enabled.
            Debugger.NotifyOfCrossThreadDependency();

            // We will attempt inline execution only if an infinite wait was requested
            // Inline execution doesn't make sense for finite timeouts and if a cancellation token was specified
            // because we don't know how long the task delegate will take.
            if (millisecondsTimeout == Timeout.Infinite && !cancellationToken.CanBeCanceled &&
                WrappedTryRunInline() && IsCompleted) // TryRunInline doesn't guarantee completion, as there may be unfinished children.
            {
                returnValue = true;
            }
            else
            {
                returnValue = SpinThenBlockingWait(millisecondsTimeout, cancellationToken);
            }

            // ETW event for Task Wait End
            if (etwIsEnabled)
            {
                Task? currentTask = InternalCurrent;
                if (currentTask != null)
                {
                    log.TaskWaitEnd(currentTask.m_taskScheduler!.Id, currentTask.Id, this.Id);
                }
                else
                {
                    log.TaskWaitEnd(TaskScheduler.Default.Id, 0, this.Id);
                }
                // logically the continuation is empty so we immediately fire
                log.TaskWaitContinuationComplete(this.Id);
            }

            return returnValue;
        }

        /// <summary>
        /// Cancels the <see cref="Task"/>.
        /// </summary>
        internal void InternalCancel()
        {
            TaskSchedulerException? tse = null;
            bool popped = false;

            // If started, and running in a task context, we can try to pop the chore.
            if ((m_stateFlags & (int)TaskStateFlags.Started) != 0)
            {
                TaskScheduler? ts = m_taskScheduler;
                try
                {
                    popped = (ts != null) && ts.TryDequeue(this);
                }
                catch (Exception e)
                {
                    // TryDequeue threw. We don't know whether the task was properly dequeued or not. So we must let the rest of
                    // the cancellation logic run its course (record the request, attempt atomic state transition and do cleanup where appropriate)
                    // Here we will only record a TaskSchedulerException, which will later be thrown at function exit.
                    tse = new TaskSchedulerException(e);
                }
            }

            // Record the cancellation request.
            RecordInternalCancellationRequest();

            // Determine whether we need to clean up
            // This will be the case
            //     1) if we were able to pop, and we are able to update task state to TaskStateFlags.Canceled
            //     2) if the task seems to be yet unstarted, and we can transition to
            //        TaskStateFlags.Canceled before anyone else can transition into _STARTED or _CANCELED or
            //        _RAN_TO_COMPLETION or _FAULTED
            // Note that we do not check for TaskStateFlags.CompletionReserved.  That only applies to promise-style
            // tasks, and a promise-style task should not enter into this codepath.
            bool mustCleanup = false;
            if (popped)
            {
                // Include TaskStateFlags.DelegateInvoked in "illegal" bits to protect against the situation where
                // TS.TryDequeue() returns true but the task is still left on the queue.
                mustCleanup = AtomicStateUpdate((int)TaskStateFlags.Canceled, (int)TaskStateFlags.Canceled | (int)TaskStateFlags.DelegateInvoked);
            }
            else if ((m_stateFlags & (int)TaskStateFlags.Started) == 0)
            {
                mustCleanup = AtomicStateUpdate((int)TaskStateFlags.Canceled,
                    (int)TaskStateFlags.Canceled | (int)TaskStateFlags.Started | (int)TaskStateFlags.RanToCompletion |
                    (int)TaskStateFlags.Faulted | (int)TaskStateFlags.DelegateInvoked);
            }

            // do the cleanup (i.e. set completion event and finish continuations)
            if (mustCleanup)
            {
                CancellationCleanupLogic();
            }

            if (tse != null)
            {
                throw tse;
            }
        }

        /// <summary>
        /// Cancels a ContinueWith task as part of determining to see whether the continuation should run.
        /// This is only valid if the ContinueWith has a default token associated with it.
        /// </summary>
        internal void InternalCancelContinueWithInitialState()
        {
            // There isn't a cancellation token assigned to the task, which means the task is still going to be in its
            // initial state.  The only way it could have transitioned is if:
            // - it was Start'd, which isn't valid for a continuation
            // - it was canceled, which won't have happened without a token
            // - it was run as a continuation, which won't have happened because this method is only invoked once
            // As a result, we can take an optimized path that avoids inflating contingent properties.
            const int IllegalFlags = (int)TaskStateFlags.Started | (int)TaskStateFlags.CompletedMask | (int)TaskStateFlags.DelegateInvoked;

            m_stateFlags |= (int)TaskStateFlags.Canceled; // no synchronization necessary, per above comment
            CancellationCleanupLogic();
        }

        // Breaks out logic for recording a cancellation request
        internal void RecordInternalCancellationRequest()
        {
            // Record the cancellation request.
            EnsureContingentPropertiesInitialized().m_internalCancellationRequested = CANCELLATION_REQUESTED;
        }

        // Breaks out logic for recording a cancellation request
        // This overload should only be used for promise tasks where no cancellation token
        // was supplied when the task was created.
        internal void RecordInternalCancellationRequest(CancellationToken tokenToRecord, object? cancellationException)
        {
            RecordInternalCancellationRequest();

            // Store the supplied cancellation token as this task's token.
            // Waiting on this task will then result in an OperationCanceledException containing this token.
            if (tokenToRecord != default)
            {
                m_contingentProperties.m_cancellationToken = tokenToRecord;
            }

            // Store the supplied cancellation exception
            if (cancellationException != null)
            {
                AddException(cancellationException, representsCancellation: true);
            }
        }

        // ASSUMES THAT A SUCCESSFUL CANCELLATION HAS JUST OCCURRED ON THIS TASK!!!
        // And this method should be called at most once per task.
        internal void CancellationCleanupLogic()
        {
            // We'd like to be able to:
            //     Debug.Assert((m_completionEvent == null) || !m_completionEvent.IsSet, "Task.CancellationCleanupLogic(): Completion event already set.");
            // However, there is a small window for a race condition.  If someone calls Wait() between InternalCancel() and
            // here, that will set m_completionEvent, leading to a meaningless/harmless assertion.

            // This may have been set already, but we need to make sure.
            Interlocked.Exchange(ref m_stateFlags, m_stateFlags | (int)TaskStateFlags.Canceled);

            // Fire completion event if it has been lazily initialized
            ContingentProperties? cp = Volatile.Read(ref m_contingentProperties);
            if (cp != null)
            {
                cp.SetCompleted();
                cp.UnregisterCancellationCallback();
            }

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Canceled);

            if (s_asyncDebuggingEnabled)
                RemoveFromActiveTasks(this);

            // Notify parents, fire continuations, other cleanup.
            FinishStageThree();
        }

        /// <summary>
        /// Sets the task's cancellation acknowledged flag.
        /// </summary>
        private void SetCancellationAcknowledged()
        {
            m_stateFlags |= (int)TaskStateFlags.CancellationAcknowledged;
        }

        /// <summary>Completes a promise task as RanToCompletion.</summary>
        /// <remarks>If this is a Task{T}, default(T) is the implied result.</remarks>
        /// <returns>true if the task was transitioned to ran to completion; false if it was already completed.</returns>
        internal bool TrySetResult()
        {
            if (AtomicStateUpdate(
                (int)TaskStateFlags.CompletionReserved | (int)TaskStateFlags.RanToCompletion,
                (int)TaskStateFlags.CompletionReserved | (int)TaskStateFlags.RanToCompletion | (int)TaskStateFlags.Faulted | (int)TaskStateFlags.Canceled))
            {
                ContingentProperties? props = m_contingentProperties;
                if (props != null)
                {
                    NotifyParentIfPotentiallyAttachedTask();
                    props.SetCompleted();
                }
                FinishContinuations();
                return true;
            }

            return false;
        }

        // Allow multiple exceptions to be assigned to a promise-style task.
        // This is useful when a TaskCompletionSource<T> stands in as a proxy
        // for a "real" task (as we do in Unwrap(), ContinueWhenAny() and ContinueWhenAll())
        // and the "real" task ends up with multiple exceptions, which is possible when
        // a task has children.
        //
        // Called from TaskCompletionSource<T>.SetException(IEnumerable<Exception>).
        internal bool TrySetException(object exceptionObject)
        {
            bool returnValue = false;

            // "Reserve" the completion for this task, while making sure that: (1) No prior reservation
            // has been made, (2) The result has not already been set, (3) An exception has not previously
            // been recorded, and (4) Cancellation has not been requested.
            //
            // If the reservation is successful, then add the exception(s) and finish completion processing.
            //
            // The lazy initialization may not be strictly necessary, but I'd like to keep it here
            // anyway.  Some downstream logic may depend upon an inflated m_contingentProperties.
            EnsureContingentPropertiesInitialized();
            if (AtomicStateUpdate(
                (int)TaskStateFlags.CompletionReserved,
                (int)TaskStateFlags.CompletionReserved | (int)TaskStateFlags.RanToCompletion | (int)TaskStateFlags.Faulted | (int)TaskStateFlags.Canceled))
            {
                AddException(exceptionObject); // handles singleton exception or exception collection
                Finish(false);
                returnValue = true;
            }

            return returnValue;
        }

        // internal helper function breaks out logic used by TaskCompletionSource and AsyncMethodBuilder
        // If the tokenToRecord is not None, it will be stored onto the task.
        // This method is only valid for promise tasks.
        internal bool TrySetCanceled(CancellationToken tokenToRecord)
        {
            return TrySetCanceled(tokenToRecord, null);
        }

        // internal helper function breaks out logic used by TaskCompletionSource and AsyncMethodBuilder
        // If the tokenToRecord is not None, it will be stored onto the task.
        // If the OperationCanceledException is not null, it will be stored into the task's exception holder.
        // This method is only valid for promise tasks.
        internal bool TrySetCanceled(CancellationToken tokenToRecord, object? cancellationException)
        {
            bool returnValue = false;

            // "Reserve" the completion for this task, while making sure that: (1) No prior reservation
            // has been made, (2) The result has not already been set, (3) An exception has not previously
            // been recorded, and (4) Cancellation has not been requested.
            //
            // If the reservation is successful, then record the cancellation and finish completion processing.
            if (AtomicStateUpdate(
                (int)TaskStateFlags.CompletionReserved,
                (int)TaskStateFlags.CompletionReserved | (int)TaskStateFlags.Canceled | (int)TaskStateFlags.Faulted | (int)TaskStateFlags.RanToCompletion))
            {
                RecordInternalCancellationRequest(tokenToRecord, cancellationException);
                CancellationCleanupLogic(); // perform cancellation cleanup actions
                returnValue = true;
            }

            return returnValue;
        }

        //
        // Continuation passing functionality (aka ContinueWith)
        //

        /// <summary>
        /// Runs all of the continuations, as appropriate.
        /// </summary>
        internal void FinishContinuations()
        {
            // Atomically store the fact that this task is completing.  From this point on, the adding of continuations will
            // result in the continuations being run/launched directly rather than being added to the continuation list.
            // Then if we grabbed any continuations, run them.
            object? continuationObject = Interlocked.Exchange(ref m_continuationObject, s_taskCompletionSentinel);
            if (continuationObject != null)
            {
                RunContinuations(continuationObject);
            }
        }

        private void RunContinuations(object continuationObject) // separated out of FinishContinuations to enable it to be inlined
        {
            TplEventSource log = TplEventSource.Log;
            bool etwIsEnabled = log.IsEnabled();
            if (etwIsEnabled)
                log.TraceSynchronousWorkBegin(this.Id, CausalitySynchronousWork.CompletionNotification);

            bool canInlineContinuations =
                (m_stateFlags & (int)TaskCreationOptions.RunContinuationsAsynchronously) == 0 &&
                RuntimeHelpers.TryEnsureSufficientExecutionStack();

            switch (continuationObject)
            {
#if !MONO
                // Runtime async continuation is very cheap to check for since
                // it is sealed. Its semantics cannot be described by
                // ITaskCompletionAction because it should be inlined when
                // there is only one, but run in parallel when there are
                // multiple.
                case RuntimeAsyncTaskContinuation tc:
                    tc.Execute(canInline: canInlineContinuations);
                    LogFinishCompletionNotification();
                    return;
#endif

                // Handle the single IAsyncStateMachineBox case.  This could be handled as part of the ITaskCompletionAction
                // but we want to ensure that inlining is properly handled in the face of schedulers, so its behavior
                // needs to be customized ala raw Actions.  This is also one of the most important cases, as it represents the
                // most common form of continuation, so we check it early.
                case IAsyncStateMachineBox stateMachineBox:
                    AwaitTaskContinuation.RunOrScheduleAction(stateMachineBox, canInlineContinuations);
                    LogFinishCompletionNotification();
                    return;

                // Handle the single Action case.
                case Action action:
                    AwaitTaskContinuation.RunOrScheduleAction(action, canInlineContinuations);
                    LogFinishCompletionNotification();
                    return;

                // Handle the single TaskContinuation case.
                case TaskContinuation tc:
                    tc.Run(this, canInlineContinuations);
                    LogFinishCompletionNotification();
                    return;

                // Handle the single ITaskCompletionAction case.
                case ITaskCompletionAction completionAction:
                    RunOrQueueCompletionAction(completionAction, canInlineContinuations);
                    LogFinishCompletionNotification();
                    return;
            }

            // Not a single; it must be a list.
            List<object?> list = (List<object?>)continuationObject;

            //
            // Begin processing of continuation list
            //

            // Wait for any concurrent adds or removes to be retired
            Monitor.Enter(list);
            Monitor.Exit(list);
            Span<object?> continuations = CollectionsMarshal.AsSpan(list);

            // Fire the asynchronous continuations first. However, if we're not able to run any continuations synchronously,
            // then we can skip this first pass, since the second pass that tries to run everything synchronously will instead
            // run everything asynchronously anyway.
            if (canInlineContinuations)
            {
                bool forceContinuationsAsync = false;
                for (int i = 0; i < continuations.Length; i++)
                {
                    // For StandardTaskContinuations, we respect the TaskContinuationOptions.ExecuteSynchronously option,
                    // as the developer needs to explicitly opt-into running the continuation synchronously, and if they do,
                    // they get what they asked for. ITaskCompletionActions are only ever created by the runtime, and we always
                    // try to execute them synchronously. For all other continuations (related to await), we only run it synchronously
                    // if it's the first such continuation; otherwise, we force it to run asynchronously so as to not artificially
                    // delay an await continuation behind other arbitrary user code created as a previous await continuation.

                    object? currentContinuation = continuations[i];
                    if (currentContinuation == null)
                    {
                        // The continuation was unregistered and null'd out, so just skip it.
                        continue;
                    }
                    else if (currentContinuation is ContinueWithTaskContinuation stc)
                    {
                        if ((stc.m_options & TaskContinuationOptions.ExecuteSynchronously) == 0)
                        {
                            continuations[i] = null; // so that we can skip this later
                            if (etwIsEnabled)
                                log.RunningContinuationList(Id, i, stc);
                            stc.Run(this, canInlineContinuationTask: false);
                        }
                    }
                    else if (currentContinuation is not ITaskCompletionAction)
                    {
                        if (forceContinuationsAsync)
                        {
                            continuations[i] = null;
                            if (etwIsEnabled)
                                log.RunningContinuationList(Id, i, currentContinuation);
                            switch (currentContinuation)
                            {
#if !MONO
                                case RuntimeAsyncTaskContinuation tc:
                                    tc.Execute(canInline: false);
                                    break;
#endif

                                case IAsyncStateMachineBox stateMachineBox:
                                    AwaitTaskContinuation.RunOrScheduleAction(stateMachineBox, allowInlining: false);
                                    break;

                                case Action action:
                                    AwaitTaskContinuation.RunOrScheduleAction(action, allowInlining: false);
                                    break;

                                default:
                                    Debug.Assert(currentContinuation is TaskContinuation);
                                    ((TaskContinuation)currentContinuation).Run(this, canInlineContinuationTask: false);
                                    break;
                            }
                        }
                        forceContinuationsAsync = true;
                    }
                }
            }

            // ... and then fire the synchronous continuations (if there are any).
            for (int i = 0; i < continuations.Length; i++)
            {
                object? currentContinuation = continuations[i];
                if (currentContinuation == null)
                {
                    continue;
                }
                continuations[i] = null; // to enable free'ing up memory earlier
                if (etwIsEnabled)
                    log.RunningContinuationList(Id, i, currentContinuation);

                switch (currentContinuation)
                {
#if !MONO
                    case RuntimeAsyncTaskContinuation tc:
                        tc.Execute(canInlineContinuations);
                        break;
#endif

                    case IAsyncStateMachineBox stateMachineBox:
                        AwaitTaskContinuation.RunOrScheduleAction(stateMachineBox, canInlineContinuations);
                        break;

                    case Action action:
                        AwaitTaskContinuation.RunOrScheduleAction(action, canInlineContinuations);
                        break;

                    case TaskContinuation tc:
                        tc.Run(this, canInlineContinuations);
                        break;

                    default:
                        Debug.Assert(currentContinuation is ITaskCompletionAction);
                        RunOrQueueCompletionAction((ITaskCompletionAction)currentContinuation, canInlineContinuations);
                        break;
                }
            }

            LogFinishCompletionNotification();
        }

        private void RunOrQueueCompletionAction(ITaskCompletionAction completionAction, bool allowInlining)
        {
            if (allowInlining || !completionAction.InvokeMayRunArbitraryCode)
            {
                completionAction.Invoke(this);
            }
            else
            {
                ThreadPool.UnsafeQueueUserWorkItemInternal(new CompletionActionInvoker(completionAction, this), preferLocal: true);
            }
        }

        private static void LogFinishCompletionNotification()
        {
            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceSynchronousWorkEnd(CausalitySynchronousWork.CompletionNotification);
        }

        #region Continuation methods

        #region Action<Task> continuation
        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task> continuationAction)
        {
            return ContinueWith(continuationAction, TaskScheduler.Current, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="cancellationToken"> The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task> continuationAction, CancellationToken cancellationToken)
        {
            return ContinueWith(continuationAction, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes.  When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task> continuationAction, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the continuation criteria specified through the <paramref
        /// name="continuationOptions"/> parameter are not met, the continuation task will be canceled
        /// instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task ContinueWith(Action<Task> continuationAction, TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationAction, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the criteria specified through the <paramref name="continuationOptions"/> parameter
        /// are not met, the continuation task will be canceled instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task> continuationAction, CancellationToken cancellationToken,
                                 TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, just with a stack mark parameter.
        private Task ContinueWith(Action<Task> continuationAction, TaskScheduler scheduler,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions)
        {
            // Throw on continuation with null action
            if (continuationAction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationAction);
            }

            // Throw on continuation with null TaskScheduler
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(continuationOptions, out TaskCreationOptions creationOptions, out InternalTaskOptions internalOptions);

            Task continuationTask = new ContinuationTaskFromTask(
                this, continuationAction, null,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }
        #endregion

        #region Action<Task, Object> continuation

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task, object?> continuationAction, object? state)
        {
            return ContinueWith(continuationAction, state, TaskScheduler.Current, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="cancellationToken"> The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task, object?> continuationAction, object? state, CancellationToken cancellationToken)
        {
            return ContinueWith(continuationAction, state, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes.  When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task, object?> continuationAction, object? state, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, state, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the continuation criteria specified through the <paramref
        /// name="continuationOptions"/> parameter are not met, the continuation task will be canceled
        /// instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task ContinueWith(Action<Task, object?> continuationAction, object? state, TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationAction, state, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the criteria specified through the <paramref name="continuationOptions"/> parameter
        /// are not met, the continuation task will be canceled instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task, object?> continuationAction, object? state, CancellationToken cancellationToken,
                                 TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, state, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, just with a stack mark parameter.
        private Task ContinueWith(Action<Task, object?> continuationAction, object? state, TaskScheduler scheduler,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions)
        {
            // Throw on continuation with null action
            if (continuationAction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationAction);
            }

            // Throw on continuation with null TaskScheduler
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(continuationOptions, out TaskCreationOptions creationOptions, out InternalTaskOptions internalOptions);

            Task continuationTask = new ContinuationTaskFromTask(
                this, continuationAction, state,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }

        #endregion

        #region Func<Task, TResult> continuation

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction)
        {
            return ContinueWith(continuationFunction, TaskScheduler.Current, default,
                TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, CancellationToken cancellationToken)
        {
            return ContinueWith(continuationFunction, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes.  When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed. If the continuation criteria specified through the <paramref
        /// name="continuationOptions"/> parameter are not met, the continuation task will be canceled
        /// instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationFunction, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed. If the criteria specified through the <paramref name="continuationOptions"/> parameter
        /// are not met, the continuation task will be canceled instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, CancellationToken cancellationToken,
                                                   TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, just with a stack mark parameter.
        private Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, TaskScheduler scheduler,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions)
        {
            // Throw on continuation with null function
            if (continuationFunction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);
            }

            // Throw on continuation with null task scheduler
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(continuationOptions, out TaskCreationOptions creationOptions, out InternalTaskOptions internalOptions);

            Task<TResult> continuationTask = new ContinuationResultTaskFromTask<TResult>(
                this, continuationFunction, null,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }
        #endregion

        #region Func<Task, Object, TResult> continuation

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state)
        {
            return ContinueWith(continuationFunction, state, TaskScheduler.Current, default,
                TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, CancellationToken cancellationToken)
        {
            return ContinueWith(continuationFunction, state, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes.  When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, state, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed. If the continuation criteria specified through the <paramref
        /// name="continuationOptions"/> parameter are not met, the continuation task will be canceled
        /// instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationFunction, state, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task"/> completes.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TResult}"/> will not be scheduled for execution until the current task has
        /// completed. If the criteria specified through the <paramref name="continuationOptions"/> parameter
        /// are not met, the continuation task will be canceled instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, CancellationToken cancellationToken,
                                                   TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, state, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, just with a stack mark parameter.
        private Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, TaskScheduler scheduler,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions)
        {
            // Throw on continuation with null function
            if (continuationFunction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);
            }

            // Throw on continuation with null task scheduler
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(continuationOptions, out TaskCreationOptions creationOptions, out InternalTaskOptions internalOptions);

            Task<TResult> continuationTask = new ContinuationResultTaskFromTask<TResult>(
                this, continuationFunction, state,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }
        #endregion

        /// <summary>
        /// Converts TaskContinuationOptions to TaskCreationOptions, and also does
        /// some validity checking along the way.
        /// </summary>
        /// <param name="continuationOptions">Incoming TaskContinuationOptions</param>
        /// <param name="creationOptions">Outgoing TaskCreationOptions</param>
        /// <param name="internalOptions">Outgoing InternalTaskOptions</param>
        internal static void CreationOptionsFromContinuationOptions(
            TaskContinuationOptions continuationOptions,
            out TaskCreationOptions creationOptions,
            out InternalTaskOptions internalOptions)
        {
            // This is used a couple of times below
            const TaskContinuationOptions NotOnAnything =
                TaskContinuationOptions.NotOnCanceled |
                TaskContinuationOptions.NotOnFaulted |
                TaskContinuationOptions.NotOnRanToCompletion;

            const TaskContinuationOptions CreationOptionsMask =
                TaskContinuationOptions.PreferFairness |
                TaskContinuationOptions.LongRunning |
                TaskContinuationOptions.DenyChildAttach |
                TaskContinuationOptions.HideScheduler |
                TaskContinuationOptions.AttachedToParent |
                TaskContinuationOptions.RunContinuationsAsynchronously;

            // Check that LongRunning and ExecuteSynchronously are not specified together
            const TaskContinuationOptions IllegalMask = TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.LongRunning;
            if ((continuationOptions & IllegalMask) == IllegalMask)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.continuationOptions, ExceptionResource.Task_ContinueWith_ESandLR);
            }

            // Check that no illegal options were specified
            if ((continuationOptions &
                ~(CreationOptionsMask | NotOnAnything |
                    TaskContinuationOptions.LazyCancellation | TaskContinuationOptions.ExecuteSynchronously)) != 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.continuationOptions);
            }

            // Check that we didn't specify "not on anything"
            if ((continuationOptions & NotOnAnything) == NotOnAnything)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.continuationOptions, ExceptionResource.Task_ContinueWith_NotOnAnything);
            }

            // This passes over all but LazyCancellation, which has no representation in TaskCreationOptions
            creationOptions = (TaskCreationOptions)(continuationOptions & CreationOptionsMask);

            // internalOptions has at least ContinuationTask and possibly LazyCancellation
            internalOptions = (continuationOptions & TaskContinuationOptions.LazyCancellation) != 0 ?
                InternalTaskOptions.ContinuationTask | InternalTaskOptions.LazyCancellation :
                InternalTaskOptions.ContinuationTask;
        }

        /// <summary>
        /// Registers the continuation and possibly runs it (if the task is already finished).
        /// </summary>
        /// <param name="continuationTask">The continuation task itself.</param>
        /// <param name="scheduler">TaskScheduler with which to associate continuation task.</param>
        /// <param name="options">Restrictions on when the continuation becomes active.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        internal void ContinueWithCore(Task continuationTask,
                                       TaskScheduler scheduler,
                                       CancellationToken cancellationToken,
                                       TaskContinuationOptions options)
        {
            // Create a TaskContinuation
            var continuation = new ContinueWithTaskContinuation(continuationTask, options, scheduler);

            // If cancellationToken is cancellable, then assign it.
            if (cancellationToken.CanBeCanceled)
            {
                if (IsCompleted || cancellationToken.IsCancellationRequested)
                {
                    // If the antecedent has completed, then we will not be queuing up
                    // the continuation in the antecedent's continuation list.  Likewise,
                    // if the cancellationToken has been canceled, continuationTask will
                    // be completed in the AssignCancellationToken call below, and there
                    // is no need to queue the continuation to the antecedent's continuation
                    // list.  In either of these two cases, we will pass "null" for the antecedent,
                    // meaning "the cancellation callback should not attempt to remove the
                    // continuation from its antecedent's continuation list".
                    continuationTask.AssignCancellationToken(cancellationToken, null, null);
                }
                else
                {
                    // The antecedent is not yet complete, so there is a pretty good chance
                    // that the continuation will be queued up in the antecedent.  Assign the
                    // cancellation token with information about the antecedent, so that the
                    // continuation can be dequeued upon the signalling of the token.
                    //
                    // It's possible that the antecedent completes before the call to AddTaskContinuation,
                    // and that is a benign race condition.  It just means that the cancellation will result in
                    // a futile search of the antecedent's continuation list.
                    continuationTask.AssignCancellationToken(cancellationToken, this, continuation);
                }
            }

            // In the case of a pre-canceled token, continuationTask will have been completed
            // in a Canceled state by now.  If such is the case, there is no need to go through
            // the motions of queuing up the continuation for eventual execution.
            if (!continuationTask.IsCompleted)
            {
                // We need additional correlation produced here to ensure that at least the continuation
                // code will be correlatable to the current activity that initiated "this" task:
                //  . when the antecendent ("this") is a promise we have very little control over where
                //    the code for the promise will run (e.g. it can be a task from a user provided
                //    TaskCompletionSource or from a classic Begin/End async operation); this user or
                //    system code will likely not have stamped an activity id on the thread, so there's
                //    generally no easy correlation that can be provided between the current activity
                //    and the promise. Also the continuation code may run practically on any thread.
                //    Since there may be no correlation between the current activity and the TCS's task
                //    activity, we ensure we at least create a correlation from the current activity to
                //    the continuation that runs when the promise completes.
                if ((this.Options & (TaskCreationOptions)InternalTaskOptions.PromiseTask) != 0 &&
                    this is not ITaskCompletionAction)
                {
                    TplEventSource log = TplEventSource.Log;
                    if (log.IsEnabled())
                    {
                        log.AwaitTaskContinuationScheduled(TaskScheduler.Current.Id, CurrentId ?? 0, continuationTask.Id);
                    }
                }

                // Attempt to enqueue the continuation
                bool continuationQueued = AddTaskContinuation(continuation, addBeforeOthers: false);

                // If the continuation was not queued (because the task completed), then run it now.
                if (!continuationQueued) continuation.Run(this, canInlineContinuationTask: true);
            }
        }
        #endregion

        // Adds a lightweight completion action to a task.  This is similar to a continuation
        // task except that it is stored as an action, and thus does not require the allocation/
        // execution resources of a continuation task.
        internal void AddCompletionAction(ITaskCompletionAction action, bool addBeforeOthers = false)
        {
            if (!AddTaskContinuation(action, addBeforeOthers))
                action.Invoke(this); // run the action directly if we failed to queue the continuation (i.e., the task completed)
        }

        internal bool TryAddCompletionAction(ITaskCompletionAction action, bool addBeforeOthers = false)
            => AddTaskContinuation(action, addBeforeOthers);

        // Support method for AddTaskContinuation that takes care of multi-continuation logic.
        // Returns true if and only if the continuation was successfully queued.
        private bool AddTaskContinuationComplex(object tc, bool addBeforeOthers)
        {
            object? oldValue = m_continuationObject;

            // Logic for the case where we were previously storing a single continuation
            List<object?>? list = oldValue as List<object?>;
            if (list is null)
            {
                // Construct a new TaskContinuation list and CAS it in.
                list = new List<object?>();
                if (addBeforeOthers)
                {
                    list.Add(tc);
                    list.Add(oldValue);
                }
                else
                {
                    list.Add(oldValue);
                    list.Add(tc);
                }

                object? expected = oldValue;
                oldValue = Interlocked.CompareExchange(ref m_continuationObject, list, expected);
                if (oldValue == expected)
                {
                    // We successfully stored the new list with both continuations in it, so we're done.
                    return true;
                }

                // We might be racing against another thread converting the single into
                // a list, or we might be racing against task completion, so recheck for list again.
                list = oldValue as List<object?>;
                if (list is null)
                {
                    return false;
                }
            }

            lock (list)
            {
                // It is possible for the task to complete right after we snap the copy of
                // the list.  If so, then return false without queuing the continuation.
                if (m_continuationObject == s_taskCompletionSentinel)
                {
                    return false;
                }

                // Before growing the list we remove possible null entries that are the
                // result from RemoveContinuations()
                if (list.Count == list.Capacity)
                {
                    list.RemoveAll(l => l == null);
                }

                if (addBeforeOthers)
                {
                    list.Insert(0, tc);
                }
                else
                {
                    list.Add(tc);
                }
            }

            return true; // continuation successfully queued, so return true.
        }

        // Record a continuation task or action.
        // Return true if and only if we successfully queued a continuation.
        internal bool AddTaskContinuation(object tc, bool addBeforeOthers)
        {
            // Make sure that, if someone calls ContinueWith() right after waiting for the predecessor to complete,
            // we don't queue up a continuation.
            if (IsCompleted) return false;

            // Try to just jam tc into m_continuationObject
            if ((m_continuationObject != null) || (Interlocked.CompareExchange(ref m_continuationObject, tc, null) != null))
            {
                // If we get here, it means that we failed to CAS tc into m_continuationObject.
                // Therefore, we must go the more complicated route.
                return AddTaskContinuationComplex(tc, addBeforeOthers);
            }
            else return true;
        }

        // Removes a continuation task from m_continuations
        internal void RemoveContinuation(object continuationObject) // could be TaskContinuation or Action<Task>
        {
            // We need to snap a local reference to m_continuations since reading a volatile object is more costly.
            // Also to prevent the value to be changed as result of a race condition with another method.
            object? continuationsLocalRef = m_continuationObject;

            // Task is completed. Nothing to do here.
            if (continuationsLocalRef == s_taskCompletionSentinel) return;

            List<object?>? continuationsLocalListRef = continuationsLocalRef as List<object?>;
            if (continuationsLocalListRef is null)
            {
                // This is not a list. If we have a single object (the one we want to remove) we try to replace it with an empty list.
                // Note we cannot go back to a null state, since it will mess up the AddTaskContinuation logic.
                continuationsLocalRef = Interlocked.CompareExchange(ref m_continuationObject, new List<object?>(), continuationObject);
                if (continuationsLocalRef != continuationObject)
                {
                    // If we fail it means that either AddContinuationComplex won the race condition and m_continuationObject is now a List
                    // that contains the element we want to remove. Or FinishContinuations set the s_taskCompletionSentinel.
                    // So we should try to get a list one more time and if it's null then there is nothing else to do.
                    continuationsLocalListRef = continuationsLocalRef as List<object?>;
                    if (continuationsLocalListRef is null)
                        return;
                }
                else
                {
                    // Exchange was successful so we can skip the last comparison
                    return;
                }
            }

            lock (continuationsLocalListRef)
            {
                // There is a small chance that this task completed since we took a local snapshot into
                // continuationsLocalRef.  In that case, just return; we don't want to be manipulating the
                // continuation list as it is being processed.
                if (m_continuationObject == s_taskCompletionSentinel) return;

                // Find continuationObject in the continuation list
                int index = continuationsLocalListRef.IndexOf(continuationObject);

                if (index >= 0)
                {
                    // null out that TaskContinuation entry, which will be interpreted as "to be cleaned up"
                    continuationsLocalListRef[index] = null;
                }
            }
        }

        //
        // Wait methods
        //

        /// <summary>
        /// Waits for all of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="AggregateException">
        /// At least one of the <see cref="Task"/> instances was canceled -or- an exception was thrown during
        /// the execution of at least one of the <see cref="Task"/> instances.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static void WaitAll(params Task[] tasks)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            bool waitResult = WaitAllCore(tasks, Timeout.Infinite, default);
        }

        /// <summary>
        /// Waits for all of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="AggregateException">
        /// At least one of the <see cref="Task"/> instances was canceled -or- an exception was thrown during
        /// the execution of at least one of the <see cref="Task"/> instances.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        public static void WaitAll(params ReadOnlySpan<Task> tasks)
        {
            bool waitResult = WaitAllCore(tasks, Timeout.Infinite, default);
        }

        /// <summary>
        /// Waits for all of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <returns>
        /// true if all of the <see cref="Task"/> instances completed execution within the allotted time;
        /// otherwise, false.
        /// </returns>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <param name="timeout">
        /// A <see cref="TimeSpan"/> that represents the number of milliseconds to wait, or a <see
        /// cref="TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="AggregateException">
        /// At least one of the <see cref="Task"/> instances was canceled -or- an exception was thrown during
        /// the execution of at least one of the <see cref="Task"/> instances.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is a negative number other than -1 milliseconds, which represents an
        /// infinite time-out -or- timeout is greater than
        /// <see cref="int.MaxValue"/>.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static bool WaitAll(Task[] tasks, TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds is < -1 or > int.MaxValue)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.timeout);
            }

            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            return WaitAllCore(tasks, (int)totalMilliseconds, default);
        }

        /// <summary>
        /// Waits for all of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <returns>
        /// true if all of the <see cref="Task"/> instances completed execution within the allotted time;
        /// otherwise, false.
        /// </returns>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or <see cref="Timeout.Infinite"/> (-1) to
        /// wait indefinitely.</param>
        /// <param name="tasks">An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="AggregateException">
        /// At least one of the <see cref="Task"/> instances was canceled -or- an exception was thrown during
        /// the execution of at least one of the <see cref="Task"/> instances.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="millisecondsTimeout"/> is a negative number other than -1, which represents an
        /// infinite time-out.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static bool WaitAll(Task[] tasks, int millisecondsTimeout)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            return WaitAllCore(tasks, millisecondsTimeout, default);
        }

        /// <summary>
        /// Waits for all of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the tasks to complete.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="AggregateException">
        /// At least one of the <see cref="Task"/> instances was canceled -or- an exception was thrown during
        /// the execution of at least one of the <see cref="Task"/> instances.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static void WaitAll(Task[] tasks, CancellationToken cancellationToken)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            WaitAllCore(tasks, Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// Waits for all of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <returns>
        /// true if all of the <see cref="Task"/> instances completed execution within the allotted time;
        /// otherwise, false.
        /// </returns>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or <see cref="Timeout.Infinite"/> (-1) to
        /// wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the tasks to complete.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="AggregateException">
        /// At least one of the <see cref="Task"/> instances was canceled -or- an exception was thrown during
        /// the execution of at least one of the <see cref="Task"/> instances.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="millisecondsTimeout"/> is a negative number other than -1, which represents an
        /// infinite time-out.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static bool WaitAll(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            return WaitAllCore(tasks, millisecondsTimeout, cancellationToken);
        }

        /// <summary>Waits for all of the provided <see cref="Task"/> objects to complete execution unless the wait is cancelled.</summary>
        /// <param name="tasks">An <see cref="IEnumerable{T}"/> of Task instances on which to wait.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the tasks to complete.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="tasks"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The <paramref name="tasks"/> argument contains a null element.</exception>
        /// <exception cref="ObjectDisposedException">One or more of the <see cref="Task"/> objects in tasks has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> was canceled.</exception>
        /// <exception cref="AggregateException">
        /// At least one of the <see cref="Task"/> instances was canceled. If a task was canceled, the <see cref="AggregateException"/>
        /// contains an <see cref="OperationCanceledException"/> in its <see cref="AggregateException.InnerExceptions"/> collection.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        public static void WaitAll(IEnumerable<Task> tasks, CancellationToken cancellationToken = default)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            ReadOnlySpan<Task> span =
                tasks is List<Task> list ? CollectionsMarshal.AsSpan(list) :
                tasks is Task[] array ? array :
                CollectionsMarshal.AsSpan(new List<Task>(tasks));

            WaitAllCore(span, Timeout.Infinite, cancellationToken);
        }

        /// <summary>Adds an element to the list, initializing the list if it's null.</summary>
        /// <typeparam name="T">Specifies the type of data stored in the list.</typeparam>
        /// <param name="item">The item to add.</param>
        /// <param name="list">The list.</param>
        /// <param name="initSize">The size to which to initialize the list if the list is null.</param>
        private static void AddToList<T>(T item, ref List<T>? list, int initSize)
        {
            list ??= new List<T>(initSize);
            list.Add(item);
        }

        /// <summary>
        /// This internal function is only meant to be called by WaitAll()
        /// If the completed task is canceled or it has other exceptions, here we will add those
        /// into the passed in exception list (which will be lazily initialized here).
        /// </summary>
        internal static void AddExceptionsForCompletedTask(ref List<Exception>? exceptions, Task t)
        {
            AggregateException? ex = t.GetExceptions(true);
            if (ex != null)
            {
                // make sure the task's exception observed status is set appropriately
                // it's possible that WaitAll was called by the parent of an attached child,
                // this will make sure it won't throw again in the implicit wait
                t.UpdateExceptionObservedStatus();

                exceptions ??= new List<Exception>(ex.InnerExceptionCount);
                exceptions.AddRange(ex.InternalInnerExceptions);
            }
        }

        /// <summary>
        /// Waits for any of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <returns>The index of the completed task in the <paramref name="tasks"/> array argument.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static int WaitAny(params Task[] tasks)
        {
            int waitResult = WaitAnyCore(tasks, Timeout.Infinite, default);
            return waitResult;
        }

        /// <summary>
        /// Waits for any of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <param name="timeout">
        /// A <see cref="TimeSpan"/> that represents the number of milliseconds to wait, or a <see
        /// cref="TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <returns>
        /// The index of the completed task in the <paramref name="tasks"/> array argument, or -1 if the
        /// timeout occurred.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is a negative number other than -1 milliseconds, which represents an
        /// infinite time-out -or- timeout is greater than
        /// <see cref="int.MaxValue"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static int WaitAny(Task[] tasks, TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.timeout);
            }

            return WaitAnyCore(tasks, (int)totalMilliseconds, default);
        }

        /// <summary>
        /// Waits for any of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for a task to complete.
        /// </param>
        /// <returns>
        /// The index of the completed task in the <paramref name="tasks"/> array argument.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static int WaitAny(Task[] tasks, CancellationToken cancellationToken)
        {
            return WaitAnyCore(tasks, Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// Waits for any of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or <see cref="Timeout.Infinite"/> (-1) to
        /// wait indefinitely.
        /// </param>
        /// <returns>
        /// The index of the completed task in the <paramref name="tasks"/> array argument, or -1 if the
        /// timeout occurred.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="millisecondsTimeout"/> is a negative number other than -1, which represents an
        /// infinite time-out.
        /// </exception>
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static int WaitAny(Task[] tasks, int millisecondsTimeout)
        {
            return WaitAnyCore(tasks, millisecondsTimeout, default);
        }

        /// <summary>
        /// Waits for any of the provided <see cref="Task"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">
        /// An array of <see cref="Task"/> instances on which to wait.
        /// </param>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or <see cref="Timeout.Infinite"/> (-1) to
        /// wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for a task to complete.
        /// </param>
        /// <returns>
        /// The index of the completed task in the <paramref name="tasks"/> array argument, or -1 if the
        /// timeout occurred.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> argument contains a null element.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="millisecondsTimeout"/> is a negative number other than -1, which represents an
        /// infinite time-out.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        [MethodImpl(MethodImplOptions.NoOptimization)]  // this is needed for the parallel debugger
        public static int WaitAny(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken) =>
            WaitAnyCore(tasks, millisecondsTimeout, cancellationToken);

        // Separated out to allow it to be optimized (caller is marked NoOptimization for VS parallel debugger
        // to be able to inspect arguments).
        private static int WaitAnyCore(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (tasks == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }
            if (millisecondsTimeout < -1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.millisecondsTimeout);
            }

            cancellationToken.ThrowIfCancellationRequested(); // early check before we make any allocations

            int signaledTaskIndex = -1;

            // Make a pass through the loop to check for any tasks that may have
            // already been completed, and to verify that no tasks are null.

            for (int taskIndex = 0; taskIndex < tasks.Length; taskIndex++)
            {
                Task task = tasks[taskIndex];

                if (task == null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Task_WaitMulti_NullTask, ExceptionArgument.tasks);
                }

                if (signaledTaskIndex == -1 && task.IsCompleted)
                {
                    // We found our first completed task.  Store it, but we can't just return here,
                    // as we still need to validate the whole array for nulls.
                    signaledTaskIndex = taskIndex;
                }
            }

            if (signaledTaskIndex == -1 && tasks.Length != 0)
            {
                signaledTaskIndex = WaitAnyBlockingCore(tasks, millisecondsTimeout, cancellationToken);
            }

            // We need to prevent the tasks array from being GC'ed until we come out of the wait.
            // This is necessary so that the Parallel Debugger can traverse it during the long wait
            // and deduce waiter/waitee relationships
            GC.KeepAlive(tasks);

            // Return the index
            return signaledTaskIndex;
        }

        #region FromResult / FromException / FromCanceled

        /// <summary>Gets a <see cref="Task{TResult}"/> that's completed successfully with the specified result.</summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="result">The result to store into the completed task.</param>
        /// <returns>The successfully completed task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // method looks long, but for a given TResult it results in a relatively small amount of asm
        [Intrinsic]
        public static unsafe Task<TResult> FromResult<TResult>(TResult result)
        {
            // The goal of this function is to be give back a cached task if possible, or to otherwise give back a new task.
            // To give back a cached task, we need to be able to evaluate the incoming result value, and we need to avoid as
            // much overhead as possible when doing so, as this function is invoked as part of the return path from every async
            // method and Task.FromResult. Most tasks won't be cached, and thus we need the checks for those that are to be as
            // close to free as possible. This requires some trickiness given the lack of generic specialization in .NET.

            if (result is null)
            {
                // null reference types and default(Nullable<T>)
                return Task<TResult>.s_defaultResultTask;
            }

            // For Boolean, we cache all possible values.
            if (typeof(TResult) == typeof(bool)) // only the relevant branches are kept for each value-type generic instantiation
            {
                Task<bool> task = *(bool*)&result ? TaskCache.s_trueTask : TaskCache.s_falseTask;
                return *(Task<TResult>*)&task;
            }

            // For Int32, we cache a range of common values, [-1,9).
            if (typeof(TResult) == typeof(int))
            {
                // Compare to constants to avoid static field access if outside of cached range.
                int value = *(int*)&result;
                if ((uint)(value - TaskCache.InclusiveInt32Min) < (TaskCache.ExclusiveInt32Max - TaskCache.InclusiveInt32Min))
                {
                    Task<int> task = TaskCache.s_int32Tasks[value - TaskCache.InclusiveInt32Min];
                    return *(Task<TResult>*)&task;
                }
            }
            else if (!RuntimeHelpers.IsReferenceOrContainsReferences<TResult>())
            {
                // For other value types, we special-case default(TResult) if we can efficiently compare bit patterns to default/0,
                // which means any value type that's 1, 2, 4, 8, or 16 bytes in size and that doesn't contain references.
                // We don't need to go through the equality operator of the TResult because we cached a task for default(TResult),
                // so we only need to confirm that this TResult has the same bits as default(TResult).
                if ((sizeof(TResult) == sizeof(byte) && *(byte*)&result == default(byte)) ||
                    (sizeof(TResult) == sizeof(ushort) && *(ushort*)&result == default(ushort)) ||
                    (sizeof(TResult) == sizeof(uint) && *(uint*)&result == default) ||
                    (sizeof(TResult) == sizeof(ulong) && *(ulong*)&result == default) ||
                    (sizeof(TResult) == sizeof(UInt128) && *(UInt128*)&result == default))
                {
                    return Task<TResult>.s_defaultResultTask;
                }
            }

            // No cached task is available.  Manufacture a new one for this result.
            return new Task<TResult>(result);
        }

        /// <summary>Creates a <see cref="Task{TResult}"/> that's completed exceptionally with the specified exception.</summary>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The faulted task.</returns>
        public static Task FromException(Exception exception)
        {
            if (exception == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exception);

            var task = new Task();
            bool succeeded = task.TrySetException(exception);
            return task;
        }

        /// <summary>Creates a <see cref="Task{TResult}"/> that's completed exceptionally with the specified exception.</summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The faulted task.</returns>
        public static Task<TResult> FromException<TResult>(Exception exception)
        {
            if (exception == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exception);

            var task = new Task<TResult>();
            bool succeeded = task.TrySetException(exception);
            return task;
        }

        /// <summary>Creates a <see cref="Task"/> that's completed due to cancellation with the specified token.</summary>
        /// <param name="cancellationToken">The token with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        public static Task FromCanceled(CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.cancellationToken);
            return new Task(true, TaskCreationOptions.None, cancellationToken);
        }

        /// <summary>Creates a <see cref="Task{TResult}"/> that's completed due to cancellation with the specified token.</summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="cancellationToken">The token with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        public static Task<TResult> FromCanceled<TResult>(CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.cancellationToken);
            return new Task<TResult>(true, default, TaskCreationOptions.None, cancellationToken);
        }

        /// <summary>Creates a <see cref="Task"/> that's completed due to cancellation with the specified exception.</summary>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        internal static Task FromCanceled(OperationCanceledException exception)
        {
            Debug.Assert(exception != null);

            var task = new Task();
            bool succeeded = task.TrySetCanceled(exception.CancellationToken, exception);
            return task;
        }

        /// <summary>Creates a <see cref="Task{TResult}"/> that's completed due to cancellation with the specified exception.</summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        internal static Task<TResult> FromCanceled<TResult>(OperationCanceledException exception)
        {
            Debug.Assert(exception != null);

            var task = new Task<TResult>();
            bool succeeded = task.TrySetCanceled(exception.CancellationToken, exception);
            return task;
        }

        #endregion

        #region Run methods

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a Task handle for that work.
        /// </summary>
        /// <param name="action">The work to execute asynchronously</param>
        /// <returns>A Task that represents the work queued to execute in the ThreadPool.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> parameter was null.
        /// </exception>
        public static Task Run(Action action)
        {
            return InternalStartNew(null, action, null, default, TaskScheduler.Default,
                TaskCreationOptions.DenyChildAttach, InternalTaskOptions.None);
        }

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a Task handle for that work.
        /// </summary>
        /// <param name="action">The work to execute asynchronously</param>
        /// <param name="cancellationToken">A cancellation token that should be used to cancel the work</param>
        /// <returns>A Task that represents the work queued to execute in the ThreadPool.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="action"/> parameter was null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The CancellationTokenSource associated with <paramref name="cancellationToken"/> was disposed.
        /// </exception>
        public static Task Run(Action action, CancellationToken cancellationToken)
        {
            return InternalStartNew(null, action, null, cancellationToken, TaskScheduler.Default,
                TaskCreationOptions.DenyChildAttach, InternalTaskOptions.None);
        }

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a Task(TResult) handle for that work.
        /// </summary>
        /// <param name="function">The work to execute asynchronously</param>
        /// <returns>A Task(TResult) that represents the work queued to execute in the ThreadPool.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="function"/> parameter was null.
        /// </exception>
        public static Task<TResult> Run<TResult>(Func<TResult> function)
        {
            return Task<TResult>.StartNew(null, function, default,
                TaskCreationOptions.DenyChildAttach, InternalTaskOptions.None, TaskScheduler.Default);
        }

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a Task(TResult) handle for that work.
        /// </summary>
        /// <param name="function">The work to execute asynchronously</param>
        /// <param name="cancellationToken">A cancellation token that should be used to cancel the work</param>
        /// <returns>A Task(TResult) that represents the work queued to execute in the ThreadPool.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="function"/> parameter was null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The CancellationTokenSource associated with <paramref name="cancellationToken"/> was disposed.
        /// </exception>
        public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)
        {
            return Task<TResult>.StartNew(null, function, cancellationToken,
                TaskCreationOptions.DenyChildAttach, InternalTaskOptions.None, TaskScheduler.Default);
        }

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a proxy for the
        /// Task returned by <paramref name="function"/>.
        /// </summary>
        /// <param name="function">The work to execute asynchronously</param>
        /// <returns>A Task that represents a proxy for the Task returned by <paramref name="function"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="function"/> parameter was null.
        /// </exception>
        public static Task Run(Func<Task?> function)
        {
            return Run(function, default);
        }

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a proxy for the
        /// Task returned by <paramref name="function"/>.
        /// </summary>
        /// <param name="function">The work to execute asynchronously</param>
        /// <param name="cancellationToken">A cancellation token that should be used to cancel the work</param>
        /// <returns>A Task that represents a proxy for the Task returned by <paramref name="function"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="function"/> parameter was null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The CancellationTokenSource associated with <paramref name="cancellationToken"/> was disposed.
        /// </exception>
        public static Task Run(Func<Task?> function, CancellationToken cancellationToken)
        {
            if (function == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.function);

            // Short-circuit if we are given a pre-canceled token
            if (cancellationToken.IsCancellationRequested)
                return FromCanceled(cancellationToken);

            // Kick off initial Task, which will call the user-supplied function and yield a Task.
            Task<Task?> task1 = Task<Task?>.Factory.StartNew(function, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

            // Create a promise-style Task to be used as a proxy for the operation
            // Set lookForOce == true so that unwrap logic can be on the lookout for OCEs thrown as faults from task1, to support in-delegate cancellation.
            UnwrapPromise<VoidTaskResult> promise = new UnwrapPromise<VoidTaskResult>(task1, lookForOce: true);

            return promise;
        }

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a proxy for the
        /// Task(TResult) returned by <paramref name="function"/>.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the proxy Task.</typeparam>
        /// <param name="function">The work to execute asynchronously</param>
        /// <returns>A Task(TResult) that represents a proxy for the Task(TResult) returned by <paramref name="function"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="function"/> parameter was null.
        /// </exception>
        public static Task<TResult> Run<TResult>(Func<Task<TResult>?> function)
        {
            return Run(function, default);
        }

        /// <summary>
        /// Queues the specified work to run on the ThreadPool and returns a proxy for the
        /// Task(TResult) returned by <paramref name="function"/>.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the proxy Task.</typeparam>
        /// <param name="function">The work to execute asynchronously</param>
        /// <param name="cancellationToken">A cancellation token that should be used to cancel the work</param>
        /// <returns>A Task(TResult) that represents a proxy for the Task(TResult) returned by <paramref name="function"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="function"/> parameter was null.
        /// </exception>
        public static Task<TResult> Run<TResult>(Func<Task<TResult>?> function, CancellationToken cancellationToken)
        {
            if (function == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.function);

            // Short-circuit if we are given a pre-canceled token
            if (cancellationToken.IsCancellationRequested)
                return FromCanceled<TResult>(cancellationToken);

            // Kick off initial Task, which will call the user-supplied function and yield a Task.
            Task<Task<TResult>?> task1 = Task<Task<TResult>?>.Factory.StartNew(function, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

            // Create a promise-style Task to be used as a proxy for the operation
            // Set lookForOce == true so that unwrap logic can be on the lookout for OCEs thrown as faults from task1, to support in-delegate cancellation.
            UnwrapPromise<TResult> promise = new UnwrapPromise<TResult>(task1, lookForOce: true);

            return promise;
        }

        #endregion

        #region Delay methods

        /// <summary>
        /// Creates a Task that will complete after a time delay.
        /// </summary>
        /// <param name="delay">The time span to wait before completing the returned Task</param>
        /// <returns>A Task that represents the time delay</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="delay"/> is less than -1 or greater than the maximum allowed timer duration.
        /// </exception>
        /// <remarks>
        /// After the specified time delay, the Task is completed in RanToCompletion state.
        /// </remarks>
        public static Task Delay(TimeSpan delay) => Delay(delay, TimeProvider.System, default);

        /// <summary>Creates a task that completes after a specified time interval.</summary>
        /// <param name="delay">The <see cref="TimeSpan"/> to wait before completing the returned task, or <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely.</param>
        /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret <paramref name="delay"/>.</param>
        /// <returns>A task that represents the time delay.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> represents a negative time interval other than <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/>'s <see cref="TimeSpan.TotalMilliseconds"/> property is greater than 4294967294.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="timeProvider"/> argument is null.</exception>
        public static Task Delay(TimeSpan delay, TimeProvider timeProvider) => Delay(delay, timeProvider, default);

        /// <summary>
        /// Creates a Task that will complete after a time delay.
        /// </summary>
        /// <param name="delay">The time span to wait before completing the returned Task</param>
        /// <param name="cancellationToken">The cancellation token that will be checked prior to completing the returned Task</param>
        /// <returns>A Task that represents the time delay</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="delay"/> is less than -1 or greater than the maximum allowed timer duration.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The <see cref="CancellationTokenSource"/> associated
        /// with <paramref name="cancellationToken"/> has already been disposed.
        /// </exception>
        /// <remarks>
        /// If the cancellation token is signaled before the specified time delay, then the Task is completed in
        /// Canceled state.  Otherwise, the Task is completed in RanToCompletion state once the specified time
        /// delay has expired.
        /// </remarks>
        public static Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
            Delay(delay, TimeProvider.System, cancellationToken);

        /// <summary>Creates a cancellable task that completes after a specified time interval.</summary>
        /// <param name="delay">The <see cref="TimeSpan"/> to wait before completing the returned task, or <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely.</param>
        /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret <paramref name="delay"/>.</param>
        /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that represents the time delay.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> represents a negative time interval other than <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/>'s <see cref="TimeSpan.TotalMilliseconds"/> property is greater than 4294967294.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="timeProvider"/> argument is null.</exception>
        public static Task Delay(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            return Delay(ValidateTimeout(delay, ExceptionArgument.delay), timeProvider, cancellationToken);
        }

        /// <summary>
        /// Creates a Task that will complete after a time delay.
        /// </summary>
        /// <param name="millisecondsDelay">The number of milliseconds to wait before completing the returned Task</param>
        /// <returns>A Task that represents the time delay</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="millisecondsDelay"/> is less than -1.
        /// </exception>
        /// <remarks>
        /// After the specified time delay, the Task is completed in RanToCompletion state.
        /// </remarks>
        public static Task Delay(int millisecondsDelay) => Delay(millisecondsDelay, default);

        /// <summary>
        /// Creates a Task that will complete after a time delay.
        /// </summary>
        /// <param name="millisecondsDelay">The number of milliseconds to wait before completing the returned Task</param>
        /// <param name="cancellationToken">The cancellation token that will be checked prior to completing the returned Task</param>
        /// <returns>A Task that represents the time delay</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="millisecondsDelay"/> is less than -1.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The <see cref="CancellationTokenSource"/> associated
        /// with <paramref name="cancellationToken"/> has already been disposed.
        /// </exception>
        /// <remarks>
        /// If the cancellation token is signaled before the specified time delay, then the Task is completed in
        /// Canceled state.  Otherwise, the Task is completed in RanToCompletion state once the specified time
        /// delay has expired.
        /// </remarks>
        public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
        {
            // Throw on non-sensical time
            if (millisecondsDelay < -1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.millisecondsDelay, ExceptionResource.Task_Delay_InvalidMillisecondsDelay);
            }

            return Delay((uint)millisecondsDelay, TimeProvider.System, cancellationToken);
        }

        private static Task Delay(uint millisecondsDelay, TimeProvider timeProvider, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? FromCanceled(cancellationToken) :
            millisecondsDelay == 0 ? CompletedTask :
            cancellationToken.CanBeCanceled ? new DelayPromiseWithCancellation(millisecondsDelay, timeProvider, cancellationToken) :
            new DelayPromise(millisecondsDelay, timeProvider);

        internal static uint ValidateTimeout(TimeSpan timeout, ExceptionArgument argument)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > Timer.MaxSupportedTimeout)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(argument, ExceptionResource.Task_InvalidTimerTimeSpan);
            }

            return (uint)totalMilliseconds;
        }

        /// <summary>Task that also stores the completion closure and logic for Task.Delay implementation.</summary>
        private class DelayPromise : Task
        {
            private static readonly TimerCallback s_timerCallback = TimerCallback;
            private readonly ITimer? _timer;

            internal DelayPromise(uint millisecondsDelay, TimeProvider timeProvider)
            {
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationBegin(this.Id, "Task.Delay", 0);

                if (s_asyncDebuggingEnabled)
                    AddToActiveTasks(this);

                if (millisecondsDelay != Timeout.UnsignedInfinite) // no need to create the timer if it's an infinite timeout
                {
                    if (timeProvider == TimeProvider.System)
                    {
                        _timer = new TimerQueueTimer(s_timerCallback, this, millisecondsDelay, Timeout.UnsignedInfinite, flowExecutionContext: false);
                    }
                    else
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            _timer = timeProvider.CreateTimer(s_timerCallback, this, TimeSpan.FromMilliseconds(millisecondsDelay), Timeout.InfiniteTimeSpan);
                        }
                    }

                    if (IsCompleted)
                    {
                        // Handle rare race condition where the timer fires prior to our having stored it into the field, in which case
                        // the timer won't have been cleaned up appropriately.  This call to close might race with the Cleanup call to Close,
                        // but Close is thread-safe and will be a nop if it's already been closed.
                        _timer.Dispose();
                    }
                }
            }

            // Separated out into a named method to improve Timer diagnostics in a debugger
            private static void TimerCallback(object? state) => ((DelayPromise)state!).CompleteTimedOut();

            private void CompleteTimedOut()
            {
                if (TrySetResult())
                {
                    Cleanup();

                    if (s_asyncDebuggingEnabled)
                        RemoveFromActiveTasks(this);

                    if (TplEventSource.Log.IsEnabled())
                        TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);
                }
            }

            protected virtual void Cleanup() => _timer?.Dispose();
        }

        /// <summary>DelayPromise that also supports cancellation.</summary>
        private sealed class DelayPromiseWithCancellation : DelayPromise
        {
            private readonly CancellationTokenRegistration _registration;

            internal DelayPromiseWithCancellation(uint millisecondsDelay, TimeProvider timeProvider, CancellationToken token)
                : base(millisecondsDelay, timeProvider)
            {
                _registration = token.UnsafeRegister(static (state, cancellationToken) =>
                {
                    var thisRef = (DelayPromiseWithCancellation)state!;

                    // Normally RunContinuationsAsynchronously is set at construction time.  We don't want to
                    // set it at construction because we want the timer firing (already on a thread pool thread with
                    // a stack in which it's fine to execute arbitrary code) to synchronously invoke any continuations
                    // from this task.  However, a cancellation request will come synchronously from a call to
                    // CancellationTokenSource.Cancel, and we don't want to invoke arbitrary continuations from
                    // this delay task as part of that Cancel call.  As such, we set RunContinuationsAsynchronously
                    // after the fact, only when the task is being completed due to cancellation.  There is a race
                    // condition here, such that if the timer also fired concurrently, it might win, in which case
                    // it might also observe this RunContinuationsAsynchronously, but that's benign.  An alternative
                    // is to make the whole cancellation registration queue, but that's visible in that the Task's
                    // IsCompleted wouldn't be set synchronously as part of IsCompleted.
                    thisRef.AtomicStateUpdate((int)TaskCreationOptions.RunContinuationsAsynchronously, 0);

                    if (thisRef.TrySetCanceled(cancellationToken))
                    {
                        thisRef.Cleanup();
                        // This path doesn't invoke RemoveFromActiveTasks or TraceOperationCompletion
                        // because that's strangely already handled inside of TrySetCanceled.
                    }
                }, this);
                if (IsCompleted)
                {
                    // Handle rare race condition where the base timer fires prior to our having stored the cancellation registration into
                    // the field, in which case the call to Cleanup won't dispose of the registration.  In such a case, Cleanup will have
                    // already been called to dispose of the base timer, so we can just call Dispose on the registration directly.  This
                    // Dispose call might also race with the base calling Cleanup, but CTR.Dispose is thread-safe.
                    _registration.Dispose();
                }
            }

            protected override void Cleanup()
            {
                _registration.Dispose();
                base.Cleanup();
            }
        }
        #endregion

        #region WhenAll
        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
        /// <remarks>
        /// <para>
        /// If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
        /// where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied tasks.
        /// </para>
        /// <para>
        /// If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the Canceled state.
        /// </para>
        /// <para>
        /// If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the RanToCompletion state.
        /// </para>
        /// <para>
        /// If the supplied array/enumerable contains no tasks, the returned task will immediately transition to a RanToCompletion
        /// state before it's returned to the caller.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> collection contained a null task.
        /// </exception>
        public static Task WhenAll(IEnumerable<Task> tasks)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            int? count = null;
            if (tasks is ICollection<Task> taskCollection)
            {
                if (tasks is Task[] taskArray)
                {
                    return WhenAll((ReadOnlySpan<Task>)taskArray);
                }

                if (tasks is List<Task> taskList)
                {
                    return WhenAll(CollectionsMarshal.AsSpan(taskList));
                }

                count = taskCollection.Count;
            }

            // Buffer the tasks into a temporary span. Small sets of tasks are common,
            // so for <= 8 we stack allocate.
            ValueListBuilder<Task> builder = count is > 8 ?
                new ValueListBuilder<Task>(count.Value) :
                new ValueListBuilder<Task>([null, null, null, null, null, null, null, null]);
            foreach (Task task in tasks)
            {
                builder.Append(task);
            }

            Task t = WhenAll(builder.AsSpan());

            builder.Dispose();
            return t;
        }

        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
        /// <remarks>
        /// <para>
        /// If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
        /// where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied tasks.
        /// </para>
        /// <para>
        /// If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the Canceled state.
        /// </para>
        /// <para>
        /// If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the RanToCompletion state.
        /// </para>
        /// <para>
        /// If the supplied array/enumerable contains no tasks, the returned task will immediately transition to a RanToCompletion
        /// state before it's returned to the caller.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task.
        /// </exception>
        public static Task WhenAll(params Task[] tasks)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            return WhenAll((ReadOnlySpan<Task>)tasks);
        }

        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
        /// <remarks>
        /// <para>
        /// If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
        /// where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied tasks.
        /// </para>
        /// <para>
        /// If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the Canceled state.
        /// </para>
        /// <para>
        /// If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the RanToCompletion state.
        /// </para>
        /// <para>
        /// If the supplied span contains no tasks, the returned task will immediately transition to a RanToCompletion
        /// state before it's returned to the caller.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">The <paramref name="tasks"/> array contained a null task.</exception>
        public static Task WhenAll(params ReadOnlySpan<Task> tasks)
        {
            switch (tasks.Length)
            {
                case 0:
                    return CompletedTask;

                case 1:
                    Task t = tasks[0];
                    if (t is null)
                    {
                        ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                    }
                    return t;

                default:
                    return new WhenAllPromise(tasks);
            }
        }

        /// <summary>A Task that gets completed when all of its constituent tasks complete.</summary>
        private sealed class WhenAllPromise : Task, ITaskCompletionAction
        {
            /// <summary>The number of tasks remaining to complete.</summary>
            private int _remainingToComplete;

            internal WhenAllPromise(ReadOnlySpan<Task> tasks)
            {
                Debug.Assert(tasks.Length != 0, "Expected a non-zero length task array");
                Debug.Assert(m_stateObject is null, "Expected to be able to use the state object field for faulted/canceled tasks.");
                m_stateFlags |= (int)InternalTaskOptions.HiddenState;

                // Throw if any of the provided tasks is null. This is best effort to inform the caller
                // they've made a mistake.  If between the time we check for nulls and the time we hook
                // up callbacks one of the entries is changed from non-null to null, we'll just ignore
                // the null at that point; any such use (e.g. calling WhenAll with an array that's mutated
                // concurrently with the synchronous call to WhenAll) is erroneous.
                foreach (Task task in tasks)
                {
                    if (task is null)
                    {
                        ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                    }
                }

                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationBegin(Id, "Task.WhenAll", 0);
                }

                if (s_asyncDebuggingEnabled)
                {
                    AddToActiveTasks(this);
                }

                _remainingToComplete = tasks.Length;

                foreach (Task task in tasks)
                {
                    if (task is null || task.IsCompleted)
                    {
                        Invoke(task); // short-circuit the completion action, if possible
                    }
                    else
                    {
                        task.AddCompletionAction(this); // simple completion action
                    }
                }
            }

            public void Invoke(Task? completedTask)
            {
                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationRelation(Id, CausalityRelation.Join);
                }

                if (completedTask is not null)
                {
                    if (completedTask.IsWaitNotificationEnabled)
                    {
                        SetNotificationForWaitCompletion(enabled: true);
                    }

                    if (!completedTask.IsCompletedSuccessfully)
                    {
                        // Try to store the completed task as the first that's failed or faulted.
                        object? failedOrCanceled = Interlocked.CompareExchange(ref m_stateObject, completedTask, null);
                        if (failedOrCanceled != null)
                        {
                            // There was already something there.
                            while (true)
                            {
                                // If it was a list, add it to the list.
                                if (failedOrCanceled is List<Task> list)
                                {
                                    lock (list)
                                    {
                                        list.Add(completedTask);
                                    }
                                    break;
                                }

                                Task first = (Task)failedOrCanceled;
                                failedOrCanceled = Interlocked.CompareExchange(ref m_stateObject, new List<Task> { first, completedTask }, first);
                                if (failedOrCanceled == first)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                // Decrement the count, and only continue to complete the promise if we're the last one.
                if (Interlocked.Decrement(ref _remainingToComplete) == 0)
                {
                    object? failedOrCanceled = m_stateObject;
                    if (failedOrCanceled is null)
                    {
                        if (TplEventSource.Log.IsEnabled())
                        {
                            TplEventSource.Log.TraceOperationEnd(Id, AsyncCausalityStatus.Completed);
                        }

                        if (s_asyncDebuggingEnabled)
                        {
                            RemoveFromActiveTasks(this);
                        }

                        bool completed = TrySetResult();
                    }
                    else
                    {
                        // Set up some accounting variables
                        List<ExceptionDispatchInfo>? observedExceptions = null;
                        Task? canceledTask = null;

                        void HandleTask(Task task)
                        {
                            if (task.IsFaulted)
                            {
                                (observedExceptions ??= new()).AddRange(task.GetExceptionDispatchInfos());
                            }
                            else if (task.IsCanceled)
                            {
                                canceledTask ??= task; // use the first task that's canceled
                            }
                        }

                        // Loop through the completed or faulted tasks:
                        //   If any one of them faults, the result will be faulted
                        //   If none fault, but at least one is canceled, the result will be canceled
                        if (failedOrCanceled is List<Task> list)
                        {
                            foreach (Task task in list)
                            {
                                HandleTask(task);
                            }
                        }
                        else
                        {
                            HandleTask((Task)failedOrCanceled);
                        }

                        if (observedExceptions != null)
                        {

                            // We don't need to TraceOperationCompleted here because TrySetException will call Finish and we'll log it there

                            TrySetException(observedExceptions);
                        }
                        else if (canceledTask != null)
                        {
                            TrySetCanceled(canceledTask.CancellationToken, canceledTask.GetCancellationExceptionDispatchInfo());
                        }
                    }

                }

            }

            public bool InvokeMayRunArbitraryCode => true;
        }

        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
        /// <remarks>
        /// <para>
        /// If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
        /// where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied tasks.
        /// </para>
        /// <para>
        /// If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the Canceled state.
        /// </para>
        /// <para>
        /// If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the RanToCompletion state.
        /// The Result of the returned task will be set to an array containing all of the results of the
        /// supplied tasks in the same order as they were provided (e.g. if the input tasks array contained t1, t2, t3, the output
        /// task's Result will return an TResult[] where arr[0] == t1.Result, arr[1] == t2.Result, and arr[2] == t3.Result).
        /// </para>
        /// <para>
        /// If the supplied array/enumerable contains no tasks, the returned task will immediately transition to a RanToCompletion
        /// state before it's returned to the caller.  The returned TResult[] will be an array of 0 elements.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> collection contained a null task.
        /// </exception>
        public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks)
        {
            // Take a more efficient route if tasks is actually an array
            if (tasks is Task<TResult>[] taskArray)
            {
                return WhenAll(taskArray);
            }

            // Skip a List allocation/copy if tasks is a collection
            if (tasks is ICollection<Task<TResult>> taskCollection)
            {
                int count = taskCollection.Count;
                if (count == 0)
                {
                    return new Task<TResult[]>(false, [], TaskCreationOptions.None, default);
                }

                taskArray = new Task<TResult>[count];
                taskCollection.CopyTo(taskArray, 0);
                foreach (Task<TResult> task in taskArray)
                {
                    if (task is null)
                    {
                        ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                    }
                }

                return new WhenAllPromise<TResult>(taskArray);
            }

            // Do some argument checking and convert tasks into a List (later an array)
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            List<Task<TResult>> taskList = new List<Task<TResult>>();
            foreach (Task<TResult> task in tasks)
            {
                if (task is null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                }

                taskList.Add(task);
            }

            return taskList.Count == 0 ?
                new Task<TResult[]>(false, [], TaskCreationOptions.None, default) :
                new WhenAllPromise<TResult>(taskList.ToArray());
        }

        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
        /// <remarks>
        /// <para>
        /// If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
        /// where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied tasks.
        /// </para>
        /// <para>
        /// If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the Canceled state.
        /// </para>
        /// <para>
        /// If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the RanToCompletion state.
        /// The Result of the returned task will be set to an array containing all of the results of the
        /// supplied tasks in the same order as they were provided (e.g. if the input tasks array contained t1, t2, t3, the output
        /// task's Result will return an TResult[] where arr[0] == t1.Result, arr[1] == t2.Result, and arr[2] == t3.Result).
        /// </para>
        /// <para>
        /// If the supplied array/enumerable contains no tasks, the returned task will immediately transition to a RanToCompletion
        /// state before it's returned to the caller.  The returned TResult[] will be an array of 0 elements.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task.
        /// </exception>
        public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks)
        {
            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            return WhenAll((ReadOnlySpan<Task<TResult>>)tasks);
        }

        /// <summary>
        /// Creates a task that will complete when all of the supplied tasks have completed.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
        /// <remarks>
        /// <para>
        /// If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
        /// where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied tasks.
        /// </para>
        /// <para>
        /// If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the Canceled state.
        /// </para>
        /// <para>
        /// If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the RanToCompletion state.
        /// The Result of the returned task will be set to an array containing all of the results of the
        /// supplied tasks in the same order as they were provided (e.g. if the input tasks array contained t1, t2, t3, the output
        /// task's Result will return an TResult[] where arr[0] == t1.Result, arr[1] == t2.Result, and arr[2] == t3.Result).
        /// </para>
        /// <para>
        /// If the supplied array/enumerable contains no tasks, the returned task will immediately transition to a RanToCompletion
        /// state before it's returned to the caller.  The returned TResult[] will be an array of 0 elements.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task.
        /// </exception>
        public static Task<TResult[]> WhenAll<TResult>(params ReadOnlySpan<Task<TResult>> tasks)
        {
            if (tasks.IsEmpty)
            {
                return new Task<TResult[]>(false, [], TaskCreationOptions.None, default);
            }

            Task<TResult>[] tasksCopy = tasks.ToArray();
            foreach (Task<TResult> task in tasksCopy)
            {
                if (task is null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                }
            }

            return new WhenAllPromise<TResult>(tasksCopy);
        }

        // A Task<T> that gets completed when all of its constituent tasks complete.
        // Completion logic will analyze the antecedents in order to choose completion status.
        // See comments for non-generic version of WhenAllPromise class.
        //
        // Used in InternalWhenAll<TResult>(Task<TResult>[])
        private sealed class WhenAllPromise<T> : Task<T[]>, ITaskCompletionAction
        {
            /// <summary>
            /// Stores all of the constituent tasks.  Tasks clear themselves out of this
            /// array as they complete, but only if they don't have their wait notification bit set.
            /// </summary>
            private readonly Task<T>?[] m_tasks;
            /// <summary>The number of tasks remaining to complete.</summary>
            private int m_count;

            internal WhenAllPromise(Task<T>[] tasks)
            {
                m_tasks = tasks;
                m_count = tasks.Length;

                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationBegin(this.Id, "Task.WhenAll", 0);

                if (s_asyncDebuggingEnabled)
                    AddToActiveTasks(this);

                foreach (Task<T> task in tasks)
                {
                    if (task.IsCompleted) this.Invoke(task); // short-circuit the completion action, if possible
                    else task.AddCompletionAction(this); // simple completion action
                }
            }

            public void Invoke(Task ignored)
            {
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationRelation(this.Id, CausalityRelation.Join);

                // Decrement the count, and only continue to complete the promise if we're the last one.
                if (Interlocked.Decrement(ref m_count) == 0)
                {
                    // Set up some accounting variables
                    T[] results = new T[m_tasks.Length];
                    List<ExceptionDispatchInfo>? observedExceptions = null;
                    Task? canceledTask = null;

                    // Loop through antecedents:
                    //   If any one of them faults, the result will be faulted
                    //   If none fault, but at least one is canceled, the result will be canceled
                    //   If none fault or are canceled, then result will be RanToCompletion
                    for (int i = 0; i < m_tasks.Length; i++)
                    {
                        Task<T>? task = m_tasks[i];

                        if (task.IsFaulted)
                        {
                            observedExceptions ??= new List<ExceptionDispatchInfo>();
                            observedExceptions.AddRange(task.GetExceptionDispatchInfos());
                        }
                        else if (task.IsCanceled)
                        {
                            canceledTask ??= task; // use the first task that's canceled
                        }
                        else
                        {
                            results[i] = task.GetResultCore(waitCompletionNotification: false); // avoid Result, which would triggering debug notification
                        }

                        // Regardless of completion state, if the task has its debug bit set, transfer it to the
                        // WhenAll task.  We must do this before we complete the task.
                        if (task.IsWaitNotificationEnabled) this.SetNotificationForWaitCompletion(enabled: true);
                        else m_tasks[i] = null; // avoid holding onto tasks unnecessarily
                    }

                    if (observedExceptions != null)
                    {
                        // We don't need to TraceOperationCompleted here because TrySetException will call Finish and we'll log it there

                        TrySetException(observedExceptions);
                    }
                    else if (canceledTask != null)
                    {
                        TrySetCanceled(canceledTask.CancellationToken, canceledTask.GetCancellationExceptionDispatchInfo());
                    }
                    else
                    {
                        if (TplEventSource.Log.IsEnabled())
                            TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);

                        if (s_asyncDebuggingEnabled)
                            RemoveFromActiveTasks(this);

                        TrySetResult(results);
                    }
                }
            }

            public bool InvokeMayRunArbitraryCode => true;

            /// <summary>
            /// Returns whether we should notify the debugger of a wait completion.  This returns true
            /// iff at least one constituent task has its bit set.
            /// </summary>
            private protected override bool ShouldNotifyDebuggerOfWaitCompletion =>
                base.ShouldNotifyDebuggerOfWaitCompletion &&
                AnyTaskRequiresNotifyDebuggerOfWaitCompletion(m_tasks);
        }
        #endregion

        #region WhenAny
        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task, or was empty.
        /// </exception>
        public static Task<Task> WhenAny(params Task[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);

            return WhenAnyCore((ReadOnlySpan<Task>)tasks);
        }

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task, or was empty.
        /// </exception>
        public static Task<Task> WhenAny(params ReadOnlySpan<Task> tasks) =>
            WhenAnyCore(tasks);

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task, or was empty.
        /// </exception>
        private static Task<TTask> WhenAnyCore<TTask>(ReadOnlySpan<TTask> tasks) where TTask : Task
        {
            if (tasks.Length == 2)
            {
                return WhenAny(tasks[0], tasks[1]);
            }

            if (tasks.IsEmpty)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_EmptyTaskList, ExceptionArgument.tasks);
            }

            // Make a defensive copy, as the user may manipulate the input collection
            // after we return but before the WhenAny asynchronously completes.
            TTask[] tasksCopy = tasks.ToArray();
            foreach (TTask task in tasksCopy)
            {
                if (task is null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                }
            }

            // Previously implemented CommonCWAnyLogic() can handle the rest
            return TaskFactory.CommonCWAnyLogic(tasksCopy);
        }

        /// <summary>Creates a task that will complete when either of the supplied tasks have completed.</summary>
        /// <param name="task1">The first task to wait on for completion.</param>
        /// <param name="task2">The second task to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="task1"/> or <paramref name="task2"/> argument was null.
        /// </exception>
        public static Task<Task> WhenAny(Task task1, Task task2) =>
            WhenAny<Task>(task1, task2);

        /// <summary>Creates a task that will complete when either of the supplied tasks have completed.</summary>
        /// <param name="task1">The first task to wait on for completion.</param>
        /// <param name="task2">The second task to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="task1"/> or <paramref name="task2"/> argument was null.
        /// </exception>
        private static Task<TTask> WhenAny<TTask>(TTask task1, TTask task2) where TTask : Task
        {
            ArgumentNullException.ThrowIfNull(task1);
            ArgumentNullException.ThrowIfNull(task2);

            return
                task1.IsCompleted ? FromResult(task1) :
                task2.IsCompleted ? FromResult(task2) :
                new TwoTaskWhenAnyPromise<TTask>(task1, task2);
        }

        /// <summary>A promise type used by WhenAny to wait on exactly two tasks.</summary>
        /// <typeparam name="TTask">Specifies the type of the task.</typeparam>
        /// <remarks>
        /// This has essentially the same logic as <see cref="TaskFactory.CompleteOnInvokePromise{TTask}"/>, but optimized
        /// for two tasks rather than any number. Exactly two tasks has shown to be the most common use-case by far.
        /// </remarks>
        private sealed class TwoTaskWhenAnyPromise<TTask> : Task<TTask>, ITaskCompletionAction where TTask : Task
        {
            private TTask? _task1, _task2;

            /// <summary>Instantiate the promise and register it with both tasks as a completion action.</summary>
            public TwoTaskWhenAnyPromise(TTask task1, TTask task2)
            {
                Debug.Assert(task1 != null && task2 != null);
                _task1 = task1;
                _task2 = task2;

                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationBegin(this.Id, "Task.WhenAny", 0);
                }

                if (s_asyncDebuggingEnabled)
                {
                    AddToActiveTasks(this);
                }

                task1.AddCompletionAction(this);

                task2.AddCompletionAction(this);
                if (task1.IsCompleted)
                {
                    // If task1 has already completed, Invoke may have tried to remove the continuation from
                    // each task before task2 added the continuation, in which case it's now referencing the
                    // already completed continuation.  To deal with that race condition, explicitly check
                    // and remove the continuation here.
                    task2.RemoveContinuation(this);
                }
            }

            /// <summary>Completes this task when one of the constituent tasks completes.</summary>
            public void Invoke(Task completingTask)
            {
                Task? task1;
                if ((task1 = Interlocked.Exchange(ref _task1, null)) != null)
                {
                    Task? task2 = _task2;
                    _task2 = null;

                    if (TplEventSource.Log.IsEnabled())
                    {
                        TplEventSource.Log.TraceOperationRelation(this.Id, CausalityRelation.Choice);
                        TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);
                    }

                    if (s_asyncDebuggingEnabled)
                    {
                        RemoveFromActiveTasks(this);
                    }

                    if (!task1.IsCompleted)
                    {
                        task1.RemoveContinuation(this);
                    }
                    else
                    {
                        task2.RemoveContinuation(this);
                    }

                    bool success = TrySetResult((TTask)completingTask);
                }
            }

            public bool InvokeMayRunArbitraryCode => true;
        }

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> collection contained a null task, or was empty.
        /// </exception>
        public static Task<Task> WhenAny(IEnumerable<Task> tasks) =>
            WhenAny<Task>(tasks);

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <typeparam name="TTask">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> collection contained a null task, or was empty.
        /// </exception>
        private static Task<TTask> WhenAny<TTask>(IEnumerable<TTask> tasks) where TTask : Task
        {
            // Skip a List allocation/copy if tasks is a collection
            if (tasks is ICollection<TTask> tasksAsCollection)
            {
                // Take a more efficient path if tasks is actually a list or an array. Arrays are a bit less common,
                // since if argument was strongly-typed as an array, it would have bound to the array-based overload.
                if (tasks.GetType() == typeof(List<TTask>))
                {
                    return WhenAnyCore((ReadOnlySpan<TTask>)CollectionsMarshal.AsSpan(Unsafe.As<List<TTask>>(tasks)));
                }
                if (tasks is TTask[] tasksAsArray)
                {
                    return WhenAnyCore((ReadOnlySpan<TTask>)tasksAsArray);
                }

                int count = tasksAsCollection.Count;
                if (count <= 0)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_EmptyTaskList, ExceptionArgument.tasks);
                }

                var taskArray = new TTask[count];
                tasksAsCollection.CopyTo(taskArray, 0);
                foreach (TTask task in taskArray)
                {
                    if (task is null)
                    {
                        ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                    }
                }

                return TaskFactory.CommonCWAnyLogic(taskArray);
            }

            if (tasks is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            }

            // Make a defensive copy, as the user may manipulate the tasks collection
            // after we return but before the WhenAny asynchronously completes.
            var taskList = new List<TTask>();
            foreach (TTask task in tasks)
            {
                if (task is null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                }

                taskList.Add(task);
            }

            if (taskList.Count == 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_EmptyTaskList, ExceptionArgument.tasks);
            }

            // Previously implemented CommonCWAnyLogic() can handle the rest
            return TaskFactory.CommonCWAnyLogic(taskList);
        }

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task, or was empty.
        /// </exception>
        public static Task<Task<TResult>> WhenAny<TResult>(params Task<TResult>[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);

            return WhenAnyCore((ReadOnlySpan<Task<TResult>>)tasks);
        }

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> array contained a null task, or was empty.
        /// </exception>
        public static Task<Task<TResult>> WhenAny<TResult>(params ReadOnlySpan<Task<TResult>> tasks) =>
            WhenAnyCore(tasks);

        /// <summary>Creates a task that will complete when either of the supplied tasks have completed.</summary>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="task1">The first task to wait on for completion.</param>
        /// <param name="task2">The second task to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="task1"/> or <paramref name="task2"/> argument was null.
        /// </exception>
        public static Task<Task<TResult>> WhenAny<TResult>(Task<TResult> task1, Task<TResult> task2) =>
            WhenAny<Task<TResult>>(task1, task2);

        /// <summary>
        /// Creates a task that will complete when any of the supplied tasks have completed.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to wait on for completion.</param>
        /// <returns>A task that represents the completion of one of the supplied tasks.  The return Task's Result is the task that completed.</returns>
        /// <remarks>
        /// The returned task will complete when any of the supplied tasks has completed.  The returned task will always end in the RanToCompletion state
        /// with its Result set to the first task to complete.  This is true even if the first task to complete ended in the Canceled or Faulted state.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="tasks"/> argument was null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The <paramref name="tasks"/> collection contained a null task, or was empty.
        /// </exception>
        public static Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks) =>
            WhenAny<Task<TResult>>(tasks);
        #endregion

        #region WhenEach
        /// <summary>Creates an <see cref="IAsyncEnumerable{T}"/> that will yield the supplied tasks as those tasks complete.</summary>
        /// <param name="tasks">The task to iterate through when completed.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> for iterating through the supplied tasks.</returns>
        /// <remarks>
        /// The supplied tasks will become available to be output via the enumerable once they've completed. The exact order
        /// in which the tasks will become available is not defined.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="tasks"/> contains a null.</exception>
        public static IAsyncEnumerable<Task> WhenEach(params Task[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            return WhenEach((ReadOnlySpan<Task>)tasks);
        }

        /// <inheritdoc cref="WhenEach(Task[])"/>
        /// <param name="tasks">The tasks to iterate through as they complete.</param>
        public static IAsyncEnumerable<Task> WhenEach(params ReadOnlySpan<Task> tasks) =>
            WhenEachState.Iterate<Task>(WhenEachState.Create(tasks));

        /// <inheritdoc cref="WhenEach(Task[])"/>
        /// <param name="tasks">The tasks to iterate through as they complete.</param>
        public static IAsyncEnumerable<Task> WhenEach(IEnumerable<Task> tasks) =>
            WhenEachState.Iterate<Task>(WhenEachState.Create(tasks));

        /// <inheritdoc cref="WhenEach(Task[])"/>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to iterate through as they complete.</param>
        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(params Task<TResult>[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            return WhenEach((ReadOnlySpan<Task<TResult>>)tasks);
        }

        /// <inheritdoc cref="WhenEach(Task[])"/>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to iterate through as they complete.</param>
        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(params ReadOnlySpan<Task<TResult>> tasks) =>
            WhenEachState.Iterate<Task<TResult>>(WhenEachState.Create(ReadOnlySpan<Task>.CastUp(tasks)));

        /// <inheritdoc cref="WhenEach(Task[])"/>
        /// <typeparam name="TResult">The type of the result returned by the tasks.</typeparam>
        /// <param name="tasks">The tasks to iterate through as they complete.</param>
        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(IEnumerable<Task<TResult>> tasks) =>
            WhenEachState.Iterate<Task<TResult>>(WhenEachState.Create(tasks));

        /// <summary>Object used by <see cref="Iterate"/> to store its state.</summary>
        private sealed class WhenEachState : Queue<Task>, IValueTaskSource, ITaskCompletionAction
        {
            /// <summary>Implementation backing the ValueTask used to wait for the next task to be available.</summary>
            /// <remarks>This is a mutable struct. Do not make it readonly.</remarks>
            private ManualResetValueTaskSourceCore<bool> _waitForNextCompletedTask = new() { RunContinuationsAsynchronously = true }; // _waitForNextCompletedTask.Set is called while holding a lock
            /// <summary>0 if this has never been used in an iteration; 1 if it has.</summary>
            /// <remarks>This is used to ensure we only ever iterate through the tasks once.</remarks>
            private int _enumerated;

            /// <summary>Called at the beginning of the iterator to assume ownership of the state.</summary>
            /// <returns>true if the caller owns the state; false if the caller should end immediately.</returns>
            public bool TryStart() => Interlocked.Exchange(ref _enumerated, 1) == 0;

            /// <summary>Gets or sets the number of tasks that haven't yet been yielded.</summary>
            public int Remaining { get; set; }

            void ITaskCompletionAction.Invoke(Task completingTask)
            {
                lock (this)
                {
                    // Enqueue the task into the queue. If the Count is now 1, we transitioned from
                    // empty to non-empty, which means we need to signal the MRVTSC, as the consumer
                    // could be waiting on a ValueTask representing a completed task being available.
                    Enqueue(completingTask);
                    if (Count == 1)
                    {
                        Debug.Assert(_waitForNextCompletedTask.GetStatus(_waitForNextCompletedTask.Version) == ValueTaskSourceStatus.Pending);
                        _waitForNextCompletedTask.SetResult(default);
                    }
                }
            }
            bool ITaskCompletionAction.InvokeMayRunArbitraryCode => false;

            // Delegate to _waitForNextCompletedTask for IValueTaskSource implementation.
            void IValueTaskSource.GetResult(short token) => _waitForNextCompletedTask.GetResult(token);
            ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _waitForNextCompletedTask.GetStatus(token);
            void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
                _waitForNextCompletedTask.OnCompleted(continuation, state, token, flags);

            /// <summary>Creates a <see cref="WhenEachState"/> from the specified tasks.</summary>
            public static WhenEachState? Create(ReadOnlySpan<Task> tasks)
            {
                WhenEachState? waiter = null;

                if (tasks.Length != 0)
                {
                    waiter = new();
                    foreach (Task task in tasks)
                    {
                        if (task is null)
                        {
                            ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                        }

                        waiter.Remaining++;
                        task.AddCompletionAction(waiter);
                    }
                }

                return waiter;
            }

            /// <inheritdoc cref="Create(ReadOnlySpan{Task})"/>
            public static WhenEachState? Create(IEnumerable<Task> tasks)
            {
                ArgumentNullException.ThrowIfNull(tasks);

                WhenEachState? waiter = null;

                IEnumerator<Task> e = tasks.GetEnumerator();
                if (e.MoveNext())
                {
                    waiter = new();
                    do
                    {
                        Task task = e.Current;
                        if (task is null)
                        {
                            ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_NullTask, ExceptionArgument.tasks);
                        }

                        waiter.Remaining++;
                        task.AddCompletionAction(waiter);
                    }
                    while (e.MoveNext());
                }

                return waiter;
            }

            /// <summary>Iterates through the tasks represented by the provided waiter.</summary>
            public static IAsyncEnumerable<T> Iterate<T>(WhenEachState? waiter, CancellationToken cancellationToken = default) where T : Task
                => new Iterator<T>(waiter, cancellationToken);

            // Runtime async has no async iterators, so this is the enumerator such an iterator would have generated
            private sealed class Iterator<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T> where T : Task
            {
                private readonly WhenEachState? _waiter;
                private readonly CancellationToken _cancellationToken;
                private bool _owner;
                private T? _current;

                public Iterator(WhenEachState? waiter, CancellationToken cancellationToken)
                {
                    _waiter = waiter;
                    _cancellationToken = cancellationToken;
                }

                public T Current => _current!;

                public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                {
                    // The enumerable could have GetAsyncEnumerator called on it multiple times. As we're dealing with Tasks that
                    // only ever transition from non-completed to completed, re-enumeration doesn't have much benefit, so we take
                    // advantage of the optimizations possible by not supporting that and simply have the semantics that, no matter
                    // how many times the enumerable is enumerated, every task is yielded only once. The original GetAsyncEnumerator
                    // call will give back all the tasks, and all subsequent iterations will be empty.
                    return new Iterator<T>(_waiter, cancellationToken.CanBeCanceled ? cancellationToken : _cancellationToken) { _owner = _waiter?.TryStart() is true };
                }

                public async ValueTask<bool> MoveNextAsync()
                {
                    WhenEachState? waiter = _waiter;
                    if (!_owner || waiter is null)
                    {
                        return false;
                    }

                    // Loop until we've yielded all tasks.
                    while (waiter.Remaining > 0)
                    {
                        // Either get the next completed task from the queue, or get a
                        // ValueTask with which to wait for the next task to complete.
                        Task? next;
                        ValueTask waitTask = default;
                        lock (waiter)
                        {
                            // Reset the MRVTSC if it was signaled, then try to dequeue a task and
                            // either return one we got or return a ValueTask that will be signaled
                            // when the next completed task is available.
                            waiter._waitForNextCompletedTask.Reset();
                            if (!waiter.TryDequeue(out next))
                            {
                                waitTask = new(waiter, waiter._waitForNextCompletedTask.Version);
                            }
                        }

                        // If we got a completed Task, yield it.
                        if (next is not null)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            waiter.Remaining--;
                            _current = (T)next;
                            return true;
                        }

                        // If we have a cancellation token and the ValueTask isn't already completed,
                        // get a Task from the ValueTask so we can use WaitAsync to make the wait cancelable.
                        // Otherwise, just await the ValueTask directly. We don't need to be concerned
                        // about suppressing exceptions, as the ValueTask is only ever completed successfully.
                        if (_cancellationToken.CanBeCanceled && !waitTask.IsCompleted)
                        {
                            waitTask = new ValueTask(waitTask.AsTask().WaitAsync(_cancellationToken));
                        }
                        await waitTask.ConfigureAwait(false);
                    }

                    return false;
                }

                public ValueTask DisposeAsync() => default;
            }
        }
        #endregion

        private static int WaitAnyBlockingCore(Task[] tasks, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            Task<Task> firstCompleted = TaskFactory.CommonCWAnyLogic(tasks, isSyncBlocking: true);
            if (firstCompleted.Wait(millisecondsTimeout, cancellationToken))
            {
                return Array.IndexOf(tasks, firstCompleted.Result);
            }

            TaskFactory.CommonCWAnyLogicCleanup(firstCompleted);
            return -1;
        }

        internal static Task<TResult> CreateUnwrapPromise<TResult>(Task outerTask, bool lookForOce)
        {
            return new UnwrapPromise<TResult>(outerTask, lookForOce);
        }

        internal object? ContinuationForDiagnostics => m_continuationObject != this ? m_continuationObject : null;

        internal virtual Delegate[]? GetDelegateContinuationsForDebugger()
        {
            // Avoid an infinite loop by making sure the continuation object is not a reference to istelf.
            if (m_continuationObject != this)
                return GetDelegatesFromContinuationObject(m_continuationObject);
            else
                return null;
        }

        private static Delegate[]? GetDelegatesFromContinuationObject(object? continuationObject)
        {
            if (continuationObject != null)
            {
                if (continuationObject is Action singleAction)
                {
                    return [AsyncMethodBuilderCore.TryGetStateMachineForDebugger(singleAction)];
                }

                if (continuationObject is TaskContinuation taskContinuation)
                {
                    return taskContinuation.GetDelegateContinuationsForDebugger();
                }

                if (continuationObject is Task continuationTask)
                {
                    Delegate[]? delegates = continuationTask.GetDelegateContinuationsForDebugger();
                    if (delegates != null)
                        return delegates;
                }

                // We need this ITaskCompletionAction after the Task because in the case of UnwrapPromise
                // the VS debugger is more interested in the continuation than the internal invoke()
                if (continuationObject is ITaskCompletionAction singleCompletionAction)
                {
                    return [new Action<Task>(singleCompletionAction.Invoke)];
                }

                if (continuationObject is List<object?> continuationList)
                {
                    List<Delegate> result = new List<Delegate>();
                    foreach (object? obj in continuationList)
                    {
                        Delegate[]? innerDelegates = GetDelegatesFromContinuationObject(obj);
                        if (innerDelegates != null)
                        {
                            foreach (Delegate del in innerDelegates)
                            {
                                if (del != null)
                                    result.Add(del);
                            }
                        }
                    }

                    return result.ToArray();
                }
            }

            return null;
        }

        // Do not remove: VS debugger calls this API directly using func-eval to populate data in the tasks window
        private static Task? GetActiveTaskFromId(int taskId)
        {
            Task? task = null;
            s_currentActiveTasks?.TryGetValue(taskId, out task);
            return task;
        }

        private sealed class SetOnInvokeMres : ManualResetEventSlim, ITaskCompletionAction
        {
            public bool InvokeMayRunArbitraryCode => false;

            internal SetOnInvokeMres()
                : base(initialState: false, 0)
            {
            }

            public void Invoke(Task completingTask)
            {
                Set();
            }
        }

        private sealed class SetOnCountdownMres : ManualResetEventSlim, ITaskCompletionAction
        {
            private int _count;

            public bool InvokeMayRunArbitraryCode => false;

            internal SetOnCountdownMres(int count)
            {
                _count = count;
            }

            public void Invoke(Task completingTask)
            {
                if (Interlocked.Decrement(ref _count) == 0)
                {
                    Set();
                }
            }
        }

        private bool SpinThenBlockingWait(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            bool flag = millisecondsTimeout == -1;
            uint num = ((!flag) ? ((uint)Environment.TickCount) : 0u);
            bool flag2 = SpinWait(millisecondsTimeout);
            if (!flag2)
            {
                ThreadPoolWorkQueue.TransferAllLocalWorkItemsToHighPriorityGlobalQueue();
                SetOnInvokeMres setOnInvokeMres = new SetOnInvokeMres();
                try
                {
                    AddCompletionAction(setOnInvokeMres, addBeforeOthers: true);
                    if (flag)
                    {
                        bool flag3 = ThreadPool.NotifyThreadBlocked();
                        try
                        {
                            flag2 = setOnInvokeMres.Wait(-1, cancellationToken);
                        }
                        finally
                        {
                            if (flag3)
                            {
                                ThreadPool.NotifyThreadUnblocked();
                            }
                        }
                    }
                    else
                    {
                        uint num2 = (uint)Environment.TickCount - num;
                        if (num2 < millisecondsTimeout)
                        {
                            bool flag4 = ThreadPool.NotifyThreadBlocked();
                            try
                            {
                                flag2 = setOnInvokeMres.Wait((int)(millisecondsTimeout - num2), cancellationToken);
                            }
                            finally
                            {
                                if (flag4)
                                {
                                    ThreadPool.NotifyThreadUnblocked();
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (!IsCompleted)
                    {
                        RemoveContinuation(setOnInvokeMres);
                    }
                }
            }
            return flag2;
        }

        private bool SpinWait(int millisecondsTimeout)
        {
            if (IsCompleted)
            {
                return true;
            }
            if (!RuntimeFeature.IsMultithreadingSupported)
            {
                return false;
            }
            if (millisecondsTimeout == 0)
            {
                return false;
            }
            int spinCountForSpinBeforeWait = System.Threading.SpinWait.SpinCountForSpinBeforeWait;
            SpinWait spinWait = default(SpinWait);
            while (spinWait.Count < spinCountForSpinBeforeWait)
            {
                spinWait.SpinOnce(-1);
                if (IsCompleted)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool WaitAllCore(ReadOnlySpan<Task> tasks, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (millisecondsTimeout < -1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.millisecondsTimeout);
            }
            cancellationToken.ThrowIfCancellationRequested();
            List<Exception> exceptions = null;
            List<Task> list = null;
            List<Task> list2 = null;
            bool flag = false;
            bool flag2 = false;
            bool flag3 = true;
            for (int num = tasks.Length - 1; num >= 0; num--)
            {
                Task task = tasks[num];
                if (task is null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Task_WaitMulti_NullTask, ExceptionArgument.tasks);
                }
                bool flag4 = task.IsCompleted;
                if (!flag4)
                {
                    if (millisecondsTimeout != -1 || cancellationToken.CanBeCanceled)
                    {
                        AddToList(task, ref list, tasks.Length);
                    }
                    else
                    {
                        flag4 = task.WrappedTryRunInline() && task.IsCompleted;
                        if (!flag4)
                        {
                            AddToList(task, ref list, tasks.Length);
                        }
                    }
                }
                if (flag4)
                {
                    if (task.IsFaulted)
                    {
                        flag = true;
                    }
                    else if (task.IsCanceled)
                    {
                        flag2 = true;
                    }
                    if (task.IsWaitNotificationEnabled)
                    {
                        AddToList(task, ref list2, 1);
                    }
                }
            }
            if (list is not null)
            {
                flag3 = WaitAllBlockingCore(list, millisecondsTimeout, cancellationToken);
                if (flag3)
                {
                    foreach (Task item in list)
                    {
                        if (item.IsFaulted)
                        {
                            flag = true;
                        }
                        else if (item.IsCanceled)
                        {
                            flag2 = true;
                        }
                        if (item.IsWaitNotificationEnabled)
                        {
                            AddToList(item, ref list2, 1);
                        }
                    }
                }
            }
            if (flag3 && list2 is not null)
            {
                using List<Task>.Enumerator enumerator = list2.GetEnumerator();
                while (enumerator.MoveNext() && !enumerator.Current.NotifyDebuggerOfWaitCompletionIfNecessary())
                {
                }
            }
            if (flag3 && (flag | flag2))
            {
                if (!flag)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                ReadOnlySpan<Task> readOnlySpan = tasks;
                for (int i = 0; i < readOnlySpan.Length; i++)
                {
                    Task t = readOnlySpan[i];
                    AddExceptionsForCompletedTask(ref exceptions, t);
                }
                ThrowHelper.ThrowAggregateException(exceptions);
            }
            return flag3;
        }

        private static bool WaitAllBlockingCore(List<Task> tasks, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            bool flag = false;
            SetOnCountdownMres setOnCountdownMres = new SetOnCountdownMres(tasks.Count);
            try
            {
                foreach (Task task in tasks)
                {
                    task.AddCompletionAction(setOnCountdownMres, addBeforeOthers: true);
                }
                flag = setOnCountdownMres.Wait(millisecondsTimeout, cancellationToken);
            }
            finally
            {
                if (!flag)
                {
                    foreach (Task task2 in tasks)
                    {
                        if (!task2.IsCompleted)
                        {
                            task2.RemoveContinuation(setOnCountdownMres);
                        }
                    }
                }
            }
            return flag;
        }
    }

    /// <summary>
    /// Represents an asynchronous operation that produces a result at some time in the future.
    /// </summary>
    /// <typeparam name="TResult">
    /// The type of the result produced by this <see cref="Task{TResult}"/>.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// <see cref="Task{TResult}"/> instances may be created in a variety of ways. The most common approach is by
    /// using the task's <see cref="Factory"/> property to retrieve a <see
    /// cref="TaskFactory{TResult}"/> instance that can be used to create tasks for several
    /// purposes. For example, to create a <see cref="Task{TResult}"/> that runs a function, the factory's StartNew
    /// method may be used:
    /// <code>
    /// // C#
    /// var t = Task&lt;int&gt;.Factory.StartNew(() => GenerateResult());
    /// - or -
    /// var t = Task.Factory.StartNew(() => GenerateResult());
    ///
    /// ' Visual Basic
    /// Dim t = Task&lt;int&gt;.Factory.StartNew(Function() GenerateResult())
    /// - or -
    /// Dim t = Task.Factory.StartNew(Function() GenerateResult())
    /// </code>
    /// </para>
    /// <para>
    /// The <see cref="Task{TResult}"/> class also provides constructors that initialize the task but that do not
    /// schedule it for execution. For performance reasons, the StartNew method should be the
    /// preferred mechanism for creating and scheduling computational tasks, but for scenarios where creation
    /// and scheduling must be separated, the constructors may be used, and the task's
    /// <see cref="Task.Start()">Start</see>
    /// method may then be used to schedule the task for execution at a later time.
    /// </para>
    /// <para>
    /// All members of <see cref="Task{TResult}"/>, except for
    /// <see cref="Task.Dispose()">Dispose</see>, are thread-safe
    /// and may be used from multiple threads concurrently.
    /// </para>
    /// </remarks>
    public class Task<TResult> : Task
    {
        /// <summary>A cached task for default(TResult).</summary>
        internal static readonly Task<TResult> s_defaultResultTask = TaskCache.CreateCacheableTask<TResult>(default);

        // The value itself, if set.
        internal TResult? m_result;

        // Construct a promise-style task without any options.
        internal Task()
        {
        }

        // Construct a promise-style task with state and options.
        internal Task(object? state, TaskCreationOptions options) :
            base(state, options, promiseStyle: true)
        {
        }


        // Construct a pre-completed Task<TResult>
        internal Task(TResult result) :
            base(false, TaskCreationOptions.None, default)
        {
            m_result = result;
        }

        internal Task(bool canceled, TResult? result, TaskCreationOptions creationOptions, CancellationToken ct)
            : base(canceled, creationOptions, ct)
        {
            if (!canceled)
            {
                m_result = result;
            }
        }

        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified function.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        public Task(Func<TResult> function)
            : this(function, null, default,
                TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }


        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified function.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to be assigned to this task.</param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Func<TResult> function, CancellationToken cancellationToken)
            : this(function, null, cancellationToken,
                TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified function and creation options.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the task's behavior.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        public Task(Func<TResult> function, TaskCreationOptions creationOptions)
            : this(function, InternalCurrentIfAttached(creationOptions), default, creationOptions, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified function and creation options.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the task's behavior.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Func<TResult> function, CancellationToken cancellationToken, TaskCreationOptions creationOptions)
            : this(function, InternalCurrentIfAttached(creationOptions), cancellationToken, creationOptions, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified function and state.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <param name="state">An object representing data to be used by the action.</param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        public Task(Func<object?, TResult> function, object? state)
            : this(function, state, null, default,
                TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified action, state, and options.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <param name="state">An object representing data to be used by the function.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to be assigned to the new task.</param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Func<object?, TResult> function, object? state, CancellationToken cancellationToken)
            : this(function, state, null, cancellationToken,
                    TaskCreationOptions.None, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified action, state, and options.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <param name="state">An object representing data to be used by the function.</param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the task's behavior.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        public Task(Func<object?, TResult> function, object? state, TaskCreationOptions creationOptions)
            : this(function, state, InternalCurrentIfAttached(creationOptions), default,
                    creationOptions, InternalTaskOptions.None, null)
        {
        }


        /// <summary>
        /// Initializes a new <see cref="Task{TResult}"/> with the specified action, state, and options.
        /// </summary>
        /// <param name="function">
        /// The delegate that represents the code to execute in the task. When the function has completed,
        /// the task's <see cref="Result"/> property will be set to return the result value of the function.
        /// </param>
        /// <param name="state">An object representing data to be used by the function.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to be assigned to the new task.</param>
        /// <param name="creationOptions">
        /// The <see cref="TaskCreationOptions">TaskCreationOptions</see> used to
        /// customize the task's behavior.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The <paramref name="function"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="creationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskCreationOptions"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task(Func<object?, TResult> function, object? state, CancellationToken cancellationToken, TaskCreationOptions creationOptions)
            : this(function, state, InternalCurrentIfAttached(creationOptions), cancellationToken,
                    creationOptions, InternalTaskOptions.None, null)
        {
        }

        /// <summary>
        /// Creates a new future object.
        /// </summary>
        /// <param name="parent">The parent task for this future.</param>
        /// <param name="valueSelector">A function that yields the future value.</param>
        /// <param name="scheduler">The task scheduler which will be used to execute the future.</param>
        /// <param name="cancellationToken">The CancellationToken for the task.</param>
        /// <param name="creationOptions">Options to control the future's behavior.</param>
        /// <param name="internalOptions">Internal options to control the future's behavior.</param>
        internal Task(Func<TResult> valueSelector, Task? parent, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, InternalTaskOptions internalOptions, TaskScheduler? scheduler) :
            base(valueSelector, null, parent, cancellationToken, creationOptions, internalOptions, scheduler)
        {
        }

        /// <summary>
        /// Creates a new future object.
        /// </summary>
        /// <param name="parent">The parent task for this future.</param>
        /// <param name="state">An object containing data to be used by the action; may be null.</param>
        /// <param name="valueSelector">A function that yields the future value.</param>
        /// <param name="cancellationToken">The CancellationToken for the task.</param>
        /// <param name="scheduler">The task scheduler which will be used to execute the future.</param>
        /// <param name="creationOptions">Options to control the future's behavior.</param>
        /// <param name="internalOptions">Internal options to control the future's behavior.</param>
        internal Task(Delegate valueSelector, object? state, Task? parent, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, InternalTaskOptions internalOptions, TaskScheduler? scheduler) :
            base(valueSelector, state, parent, cancellationToken, creationOptions, internalOptions, scheduler)
        {
        }


        // Internal method used by TaskFactory<TResult>.StartNew() methods
        internal static Task<TResult> StartNew(Task? parent, Func<TResult> function, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, InternalTaskOptions internalOptions, TaskScheduler scheduler)
        {
            if (function == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.function);
            }
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            // Create and schedule the future.
            Task<TResult> f = new Task<TResult>(function, parent, cancellationToken, creationOptions, internalOptions | InternalTaskOptions.QueuedByRuntime, scheduler);

            f.ScheduleAndStart(false);
            return f;
        }

        // Internal method used by TaskFactory<TResult>.StartNew() methods
        internal static Task<TResult> StartNew(Task? parent, Func<object?, TResult> function, object? state, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, InternalTaskOptions internalOptions, TaskScheduler scheduler)
        {
            if (function == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.function);
            }
            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            // Create and schedule the future.
            Task<TResult> f = new Task<TResult>(function, state, parent, cancellationToken, creationOptions, internalOptions | InternalTaskOptions.QueuedByRuntime, scheduler);

            f.ScheduleAndStart(false);
            return f;
        }

        // Debugger support
        private string DebuggerDisplayResultDescription =>
            IsCompletedSuccessfully ? "" + m_result : SR.TaskT_DebuggerNoResult;

        // Debugger support
        private string DebuggerDisplayMethodDescription =>
            m_action?.Method.ToString() ?? "{null}";


        // internal helper function breaks out logic used by TaskCompletionSource
        internal bool TrySetResult(TResult? result)
        {
            bool returnValue = false;

            // "Reserve" the completion for this task, while making sure that: (1) No prior reservation
            // has been made, (2) The result has not already been set, (3) An exception has not previously
            // been recorded, and (4) Cancellation has not been requested.
            //
            // If the reservation is successful, then set the result and finish completion processing.
            if (AtomicStateUpdate((int)TaskStateFlags.CompletionReserved,
                    (int)TaskStateFlags.CompletionReserved | (int)TaskStateFlags.RanToCompletion | (int)TaskStateFlags.Faulted | (int)TaskStateFlags.Canceled))
            {
                m_result = result;

                // Signal completion, for waiting tasks

                // This logic used to be:
                //     Finish(false);
                // However, that goes through a windy code path, involves many non-inlineable functions
                // and which can be summarized more concisely with the following snippet from
                // FinishStageTwo, omitting everything that doesn't pertain to TrySetResult.
                Interlocked.Exchange(ref m_stateFlags, m_stateFlags | (int)TaskStateFlags.RanToCompletion);
                ContingentProperties? props = m_contingentProperties;
                if (props != null)
                {
                    NotifyParentIfPotentiallyAttachedTask();
                    props.SetCompleted();
                }
                FinishContinuations();
                returnValue = true;
            }

            return returnValue;
        }

        // Transitions the promise task into a successfully completed state with the specified result.
        // This is dangerous, as no synchronization is used, and thus must only be used
        // before this task is handed out to any consumers, before any continuations are hooked up,
        // before its wait handle is accessed, etc.  It's use is limited to places like in FromAsync
        // where the operation completes synchronously, and thus we know we can forcefully complete
        // the task, avoiding expensive completion paths, before the task is actually given to anyone.
        internal void DangerousSetResult(TResult result)
        {
            // If we have a parent, we need to notify it of the completion.  Take the slow path to handle that.
            if (m_contingentProperties?.m_parent != null)
            {
                bool success = TrySetResult(result);
            }
            else
            {
                m_result = result;
                m_stateFlags |= (int)TaskStateFlags.RanToCompletion;
            }
        }

        /// <summary>
        /// Gets the result value of this <see cref="Task{TResult}"/>.
        /// </summary>
        /// <remarks>
        /// The get accessor for this property ensures that the asynchronous operation is complete before
        /// returning. Once the result of the computation is available, it is stored and will be returned
        /// immediately on later calls to <see cref="Result"/>.
        /// </remarks>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public TResult Result =>
            IsWaitNotificationEnabledOrNotRanToCompletion ?
                GetResultCore(waitCompletionNotification: true) :
                m_result!;

        /// <summary>
        /// Gets the result value of this <see cref="Task{TResult}"/> once the task has completed successfully.
        /// </summary>
        /// <remarks>
        /// This version of Result should only be used if the task completed successfully and if there's
        /// no debugger wait notification enabled for this task.
        /// </remarks>
        internal TResult ResultOnSuccess
        {
            get
            {
                return m_result!;
            }
        }

        // Implements Result.  Result delegates to this method if the result isn't already available.
        internal TResult GetResultCore(bool waitCompletionNotification)
        {
            // If the result has not been calculated yet, wait for it.
            if (!IsCompleted) InternalWait(Timeout.Infinite, default); // won't throw if task faulted or canceled; that's handled below

            // Notify the debugger of the wait completion if it's requested such a notification
            if (waitCompletionNotification) NotifyDebuggerOfWaitCompletionIfNecessary();

            // Throw an exception if appropriate.
            if (!IsCompletedSuccessfully) ThrowIfExceptional(includeTaskCanceledExceptions: true);


            return m_result!;
        }

        /// <summary>
        /// Provides access to factory methods for creating <see cref="Task{TResult}"/> instances.
        /// </summary>
        /// <remarks>
        /// The factory returned from <see cref="Factory"/> is a default instance
        /// of <see cref="TaskFactory{TResult}"/>, as would result from using
        /// the default constructor on the factory type.
        /// </remarks>
        public static new TaskFactory<TResult> Factory =>
            field ?? Interlocked.CompareExchange(ref field, new(), null) ?? field;

        /// <summary>
        /// Evaluates the value selector of the Task which is passed in as an object and stores the result.
        /// </summary>
        internal override void InnerInvoke()
        {
            // Invoke the delegate
            if (m_action is Func<TResult> func)
            {
                m_result = func();
                return;
            }

            if (m_action is Func<object?, TResult> funcWithState)
            {
                m_result = funcWithState(m_stateObject);
                return;
            }
        }

        #region Await Support

        /// <summary>Gets an awaiter used to await this <see cref="Task{TResult}"/>.</summary>
        /// <returns>An awaiter instance.</returns>
        public new TaskAwaiter<TResult> GetAwaiter()
        {
            return new TaskAwaiter<TResult>(this);
        }

        /// <summary>Configures an awaiter used to await this <see cref="Task{TResult}"/>.</summary>
        /// <param name="continueOnCapturedContext">
        /// true to attempt to marshal the continuation back to the original context captured; otherwise, false.
        /// </param>
        /// <returns>An object used to await this task.</returns>
        [Intrinsic]
        public new ConfiguredTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredTaskAwaitable<TResult>(this, continueOnCapturedContext ? ConfigureAwaitOptions.ContinueOnCapturedContext : ConfigureAwaitOptions.None);
        }

        /// <summary>Configures an awaiter used to await this <see cref="Task"/>.</summary>
        /// <param name="options">Options used to configure how awaits on this task are performed.</param>
        /// <returns>An object used to await this task.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The <paramref name="options"/> argument specifies an invalid value.</exception>
        [Intrinsic]
        public new ConfiguredTaskAwaitable<TResult> ConfigureAwait(ConfigureAwaitOptions options)
        {
            if ((options & ~(ConfigureAwaitOptions.ContinueOnCapturedContext |
                             ConfigureAwaitOptions.ForceYielding)) != 0)
            {
                ThrowForInvalidOptions(options);
            }

            return new ConfiguredTaskAwaitable<TResult>(this, options);

            static void ThrowForInvalidOptions(ConfigureAwaitOptions options) =>
                throw ((options & ConfigureAwaitOptions.SuppressThrowing) == 0 ?
                    new ArgumentOutOfRangeException(nameof(options)) :
                    new ArgumentOutOfRangeException(nameof(options), SR.TaskT_ConfigureAwait_InvalidOptions));
        }

        #endregion

        #region WaitAsync methods
        /// <summary>Gets a <see cref="Task{TResult}"/> that will complete when this <see cref="Task{TResult}"/> completes or when the specified <see cref="CancellationToken"/> has cancellation requested.</summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task{TResult}"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public new Task<TResult> WaitAsync(CancellationToken cancellationToken) =>
            WaitAsync(Timeout.UnsignedInfinite, TimeProvider.System, cancellationToken);

        /// <summary>Gets a <see cref="Task{TResult}"/> that will complete when this <see cref="Task{TResult}"/> completes or when the specified timeout expires.</summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <returns>The <see cref="Task{TResult}"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public new Task<TResult> WaitAsync(TimeSpan timeout) =>
            WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), TimeProvider.System, default);

        /// <summary>
        /// Gets a <see cref="Task{TResult}"/> that will complete when this <see cref="Task{TResult}"/> completes or when the specified timeout expires.
        /// </summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret <paramref name="timeout"/>.</param>
        /// <returns>The <see cref="Task{TResult}"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public new Task<TResult> WaitAsync(TimeSpan timeout, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            return WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), timeProvider, default);
        }

        /// <summary>Gets a <see cref="Task{TResult}"/> that will complete when this <see cref="Task{TResult}"/> completes, when the specified timeout expires, or when the specified <see cref="CancellationToken"/> has cancellation requested.</summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task{TResult}"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public new Task<TResult> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), TimeProvider.System, cancellationToken);

        /// <summary>
        /// Gets a <see cref="Task{TResult}"/> that will complete when this <see cref="Task{TResult}"/> completes, when the specified timeout expires, or when the specified <see cref="CancellationToken"/> has cancellation requested.
        /// </summary>
        /// <param name="timeout">The timeout after which the <see cref="Task"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
        /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret <paramref name="timeout"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task{TResult}"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public new Task<TResult> WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            return WaitAsync(ValidateTimeout(timeout, ExceptionArgument.timeout), timeProvider, cancellationToken);
        }

        private Task<TResult> WaitAsync(uint millisecondsTimeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            if (IsCompleted || (!cancellationToken.CanBeCanceled && millisecondsTimeout == Timeout.UnsignedInfinite))
            {
                return this;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled<TResult>(cancellationToken);
            }

            if (millisecondsTimeout == 0)
            {
                return FromException<TResult>(new TimeoutException());
            }

            return new CancellationPromise<TResult>(this, millisecondsTimeout, timeProvider, cancellationToken);
        }
        #endregion

        #region Continuation methods

        #region Action<Task<TResult>> continuations

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>> continuationAction)
        {
            return ContinueWith(continuationAction, TaskScheduler.Current, default, TaskContinuationOptions.None);
        }


        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>> continuationAction, CancellationToken cancellationToken)
        {
            return ContinueWith(continuationAction, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }


        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>> continuationAction, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the continuation criteria specified through the <paramref
        /// name="continuationOptions"/> parameter are not met, the continuation task will be canceled
        /// instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>> continuationAction, TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationAction, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the criteria specified through the <paramref name="continuationOptions"/> parameter
        /// are not met, the continuation task will be canceled instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>> continuationAction, CancellationToken cancellationToken,
                                 TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, only with a stack mark.
        internal Task ContinueWith(Action<Task<TResult>> continuationAction, TaskScheduler scheduler, CancellationToken cancellationToken,
                                   TaskContinuationOptions continuationOptions)
        {
            if (continuationAction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationAction);
            }

            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(
                continuationOptions,
                out TaskCreationOptions creationOptions,
                out InternalTaskOptions internalOptions);

            Task continuationTask = new ContinuationTaskFromResultTask<TResult>(
                this, continuationAction, null,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }
        #endregion

        #region Action<Task<TResult>, Object> continuations

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>, object?> continuationAction, object? state)
        {
            return ContinueWith(continuationAction, state, TaskScheduler.Current, default, TaskContinuationOptions.None);
        }


        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>, object?> continuationAction, object? state, CancellationToken cancellationToken)
        {
            return ContinueWith(continuationAction, state, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }


        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>, object?> continuationAction, object? state, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, state, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the continuation criteria specified through the <paramref
        /// name="continuationOptions"/> parameter are not met, the continuation task will be canceled
        /// instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>, object?> continuationAction, object? state, TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationAction, state, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <param name="continuationAction">
        /// An action to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation action.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task"/> will not be scheduled for execution until the current task has
        /// completed. If the criteria specified through the <paramref name="continuationOptions"/> parameter
        /// are not met, the continuation task will be canceled instead of scheduled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationAction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task ContinueWith(Action<Task<TResult>, object?> continuationAction, object? state, CancellationToken cancellationToken,
                                 TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationAction, state, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, only with a stack mark.
        internal Task ContinueWith(Action<Task<TResult>, object?> continuationAction, object? state, TaskScheduler scheduler, CancellationToken cancellationToken,
                                   TaskContinuationOptions continuationOptions)
        {
            if (continuationAction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationAction);
            }

            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(
                continuationOptions,
                out TaskCreationOptions creationOptions,
                out InternalTaskOptions internalOptions);

            Task continuationTask = new ContinuationTaskFromResultTask<TResult>(
                this, continuationAction, state,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }

        #endregion

        #region Func<Task<TResult>,TNewResult> continuations

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current
        /// task has completed, whether it completes due to running to completion successfully, faulting due
        /// to an unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction)
        {
            return ContinueWith(continuationFunction, TaskScheduler.Current, default, TaskContinuationOptions.None);
        }


        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current
        /// task has completed, whether it completes due to running to completion successfully, faulting due
        /// to an unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, CancellationToken cancellationToken)
        {
            return ContinueWith(continuationFunction, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes.  When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task as an argument.
        /// </param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// <para>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current
        /// task has completed, whether it completes due to running to completion successfully, faulting due
        /// to an unhandled exception, or exiting out early due to being canceled.
        /// </para>
        /// <para>
        /// The <paramref name="continuationFunction"/>, when executed, should return a <see
        /// cref="Task{TNewResult}"/>. This task's completion state will be transferred to the task returned
        /// from the ContinueWith call.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationFunction, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be passed as
        /// an argument this completed task.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// <para>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </para>
        /// <para>
        /// The <paramref name="continuationFunction"/>, when executed, should return a <see cref="Task{TNewResult}"/>.
        /// This task's completion state will be transferred to the task returned from the
        /// ContinueWith call.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, just with a stack mark.
        internal Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, TaskScheduler scheduler,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions)
        {
            if (continuationFunction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);
            }

            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(
                continuationOptions,
                out TaskCreationOptions creationOptions,
                out InternalTaskOptions internalOptions);

            Task<TNewResult> continuationTask = new ContinuationResultTaskFromResultTask<TResult, TNewResult>(
                this, continuationFunction, null,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }
        #endregion

        #region Func<Task<TResult>, Object,TNewResult> continuations

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current
        /// task has completed, whether it completes due to running to completion successfully, faulting due
        /// to an unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult> continuationFunction, object? state)
        {
            return ContinueWith(continuationFunction, state, TaskScheduler.Current, default, TaskContinuationOptions.None);
        }


        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current
        /// task has completed, whether it completes due to running to completion successfully, faulting due
        /// to an unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult> continuationFunction, object? state,
            CancellationToken cancellationToken)
        {
            return ContinueWith(continuationFunction, state, TaskScheduler.Current, cancellationToken, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes.  When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult> continuationFunction, object? state,
            TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, state, scheduler, default, TaskContinuationOptions.None);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// <para>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current
        /// task has completed, whether it completes due to running to completion successfully, faulting due
        /// to an unhandled exception, or exiting out early due to being canceled.
        /// </para>
        /// <para>
        /// The <paramref name="continuationFunction"/>, when executed, should return a <see
        /// cref="Task{TNewResult}"/>. This task's completion state will be transferred to the task returned
        /// from the ContinueWith call.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult> continuationFunction, object? state,
            TaskContinuationOptions continuationOptions)
        {
            return ContinueWith(continuationFunction, state, TaskScheduler.Current, default, continuationOptions);
        }

        /// <summary>
        /// Creates a continuation that executes when the target <see cref="Task{TResult}"/> completes.
        /// </summary>
        /// <typeparam name="TNewResult">
        /// The type of the result produced by the continuation.
        /// </typeparam>
        /// <param name="continuationFunction">
        /// A function to run when the <see cref="Task{TResult}"/> completes. When run, the delegate will be
        /// passed the completed task and the caller-supplied state object as arguments.
        /// </param>
        /// <param name="state">An object representing data to be used by the continuation function.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <param name="continuationOptions">
        /// Options for when the continuation is scheduled and how it behaves. This includes criteria, such
        /// as <see
        /// cref="TaskContinuationOptions.OnlyOnCanceled">OnlyOnCanceled</see>, as
        /// well as execution options, such as <see
        /// cref="TaskContinuationOptions.ExecuteSynchronously">ExecuteSynchronously</see>.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler"/> to associate with the continuation task and to use for its
        /// execution.
        /// </param>
        /// <returns>A new continuation <see cref="Task{TNewResult}"/>.</returns>
        /// <remarks>
        /// <para>
        /// The returned <see cref="Task{TNewResult}"/> will not be scheduled for execution until the current task has
        /// completed, whether it completes due to running to completion successfully, faulting due to an
        /// unhandled exception, or exiting out early due to being canceled.
        /// </para>
        /// <para>
        /// The <paramref name="continuationFunction"/>, when executed, should return a <see cref="Task{TNewResult}"/>.
        /// This task's completion state will be transferred to the task returned from the
        /// ContinueWith call.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="continuationFunction"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="continuationOptions"/> argument specifies an invalid value for <see
        /// cref="TaskContinuationOptions">TaskContinuationOptions</see>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="scheduler"/> argument is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult> continuationFunction, object? state,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            return ContinueWith(continuationFunction, state, scheduler, cancellationToken, continuationOptions);
        }

        // Same as the above overload, just with a stack mark.
        internal Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult> continuationFunction, object? state,
            TaskScheduler scheduler, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions)
        {
            if (continuationFunction == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);
            }

            if (scheduler == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
            }

            CreationOptionsFromContinuationOptions(
                continuationOptions,
                out TaskCreationOptions creationOptions,
                out InternalTaskOptions internalOptions);

            Task<TNewResult> continuationTask = new ContinuationResultTaskFromResultTask<TResult, TNewResult>(
                this, continuationFunction, state,
                creationOptions, internalOptions
            );

            // Register the continuation.  If synchronous execution is requested, this may
            // actually invoke the continuation before returning.
            ContinueWithCore(continuationTask, scheduler, cancellationToken, continuationOptions);

            return continuationTask;
        }

        #endregion

        #endregion
    }

    // Proxy class for better debugging experience
    internal sealed class SystemThreadingTasks_TaskOfTResultDebugView<TResult>
    {
        private readonly Task<TResult> m_task;

        public SystemThreadingTasks_TaskOfTResultDebugView(Task<TResult> task)
        {
            m_task = task;
        }

        public TResult Result => m_task.IsCompleted ? m_task.Result : default!;
        public object? AsyncState => m_task.AsyncState;
        public TaskCreationOptions CreationOptions => m_task.CreationOptions;
        public Exception? Exception => m_task.Exception;
        public int Id => m_task.Id;
        public bool CancellationPending => (m_task.Status == TaskStatus.WaitingToRun) && m_task.CancellationToken.IsCancellationRequested;
        public TaskStatus Status => m_task.Status;
    }

    internal sealed class CompletionActionInvoker : IThreadPoolWorkItem
    {
        private readonly ITaskCompletionAction m_action;
        private readonly Task m_completingTask;

        internal CompletionActionInvoker(ITaskCompletionAction action, Task completingTask)
        {
            m_action = action;
            m_completingTask = completingTask;
        }

        void IThreadPoolWorkItem.Execute()
        {
            m_action.Invoke(m_completingTask);
        }
    }

    // Proxy class for better debugging experience
    internal sealed class SystemThreadingTasks_TaskDebugView
    {
        private readonly Task m_task;

        public SystemThreadingTasks_TaskDebugView(Task task)
        {
            m_task = task;
        }

        public object? AsyncState => m_task.AsyncState;
        public TaskCreationOptions CreationOptions => m_task.CreationOptions;
        public Exception? Exception => m_task.Exception;
        public int Id => m_task.Id;
        public bool CancellationPending => (m_task.Status == TaskStatus.WaitingToRun) && m_task.CancellationToken.IsCancellationRequested;
        public TaskStatus Status => m_task.Status;
    }

    /// <summary>
    /// Specifies flags that control optional behavior for the creation and execution of tasks.
    /// </summary>
    // NOTE: These options are a subset of TaskContinuationsOptions, thus before adding a flag check it is
    // not already in use.
    [Flags]
    public enum TaskCreationOptions
    {
        /// <summary>
        /// Specifies that the default behavior should be used.
        /// </summary>
        None = 0x0,

        /// <summary>
        /// A hint to a <see cref="TaskScheduler">TaskScheduler</see> to schedule a
        /// task in as fair a manner as possible, meaning that tasks scheduled sooner will be more likely to
        /// be run sooner, and tasks scheduled later will be more likely to be run later.
        /// </summary>
        PreferFairness = 0x01,

        /// <summary>
        /// Specifies that a task will be a long-running, course-grained operation. It provides a hint to the
        /// <see cref="TaskScheduler">TaskScheduler</see> that oversubscription may be
        /// warranted.
        /// </summary>
        LongRunning = 0x02,

        /// <summary>
        /// Specifies that a task is attached to a parent in the task hierarchy.
        /// </summary>
        AttachedToParent = 0x04,

        /// <summary>
        /// Specifies that an InvalidOperationException will be thrown if an attempt is made to attach a child task to the created task.
        /// </summary>
        DenyChildAttach = 0x08,

        /// <summary>
        /// Prevents the ambient scheduler from being seen as the current scheduler in the created task.  This means that operations
        /// like StartNew or ContinueWith that are performed in the created task will see TaskScheduler.Default as the current scheduler.
        /// </summary>
        HideScheduler = 0x10,

        // 0x20 is already being used in TaskContinuationOptions

        /// <summary>
        /// Forces continuations added to the current task to be executed asynchronously.
        /// This option has precedence over TaskContinuationOptions.ExecuteSynchronously
        /// </summary>
        RunContinuationsAsynchronously = 0x40
    }

    /// <summary>
    /// Task creation flags which are only used internally.
    /// </summary>
    [Flags]
    internal enum InternalTaskOptions
    {
        /// <summary> Specifies "No internal task options" </summary>
        None,

        /// <summary>Used to filter out internal vs. public task creation options.</summary>
        InternalOptionsMask = 0x0000FF00,

        ContinuationTask = 0x0200,
        PromiseTask = 0x0400,

        /// <summary>
        /// The state object should not be returned from the AsyncState property.
        /// </summary>
        HiddenState = 0x0800,

        /// <summary>
        /// Store the presence of TaskContinuationOptions.LazyCancellation, since it does not directly
        /// translate into any TaskCreationOptions.
        /// </summary>
        LazyCancellation = 0x1000,

        /// <summary>Specifies that the task will be queued by the runtime before handing it over to the user.
        /// This flag will be used to skip the cancellationtoken registration step, which is only meant for unstarted tasks.</summary>
        QueuedByRuntime = 0x2000,

        /// <summary>
        /// Denotes that Dispose should be a complete nop for a Task.  Used when constructing tasks that are meant to be cached/reused.
        /// </summary>
        DoNotDispose = 0x4000
    }

    /// <summary>
    /// Specifies flags that control optional behavior for the creation and execution of continuation tasks.
    /// </summary>
    [Flags]
    public enum TaskContinuationOptions
    {
        /// <summary>
        /// Default = "Continue on any, no task options, run asynchronously"
        /// Specifies that the default behavior should be used.  Continuations, by default, will
        /// be scheduled when the antecedent task completes, regardless of the task's final <see
        /// cref="TaskStatus">TaskStatus</see>.
        /// </summary>
        None = 0,

        // These are identical to their meanings and values in TaskCreationOptions

        /// <summary>
        /// A hint to a <see cref="TaskScheduler">TaskScheduler</see> to schedule a
        /// task in as fair a manner as possible, meaning that tasks scheduled sooner will be more likely to
        /// be run sooner, and tasks scheduled later will be more likely to be run later.
        /// </summary>
        PreferFairness = 0x01,

        /// <summary>
        /// Specifies that a task will be a long-running, coarse-grained operation.  It provides
        /// a hint to the <see cref="TaskScheduler">TaskScheduler</see> that
        /// oversubscription may be warranted.
        /// </summary>
        LongRunning = 0x02,
        /// <summary>
        /// Specifies that a task is attached to a parent in the task hierarchy.
        /// </summary>
        AttachedToParent = 0x04,

        /// <summary>
        /// Specifies that an InvalidOperationException will be thrown if an attempt is made to attach a child task to the created task.
        /// </summary>
        DenyChildAttach = 0x08,
        /// <summary>
        /// Prevents the ambient scheduler from being seen as the current scheduler in the created task.  This means that operations
        /// like StartNew or ContinueWith that are performed in the created task will see TaskScheduler.Default as the current scheduler.
        /// </summary>
        HideScheduler = 0x10,

        /// <summary>
        /// In the case of continuation cancellation, prevents completion of the continuation until the antecedent has completed.
        /// </summary>
        LazyCancellation = 0x20,

        RunContinuationsAsynchronously = 0x40,

        // These are specific to continuations

        /// <summary>
        /// Specifies that the continuation task should not be scheduled if its antecedent ran to completion.
        /// This option is not valid for multi-task continuations.
        /// </summary>
        NotOnRanToCompletion = 0x10000,
        /// <summary>
        /// Specifies that the continuation task should not be scheduled if its antecedent threw an unhandled
        /// exception. This option is not valid for multi-task continuations.
        /// </summary>
        NotOnFaulted = 0x20000,
        /// <summary>
        /// Specifies that the continuation task should not be scheduled if its antecedent was canceled. This
        /// option is not valid for multi-task continuations.
        /// </summary>
        NotOnCanceled = 0x40000,
        /// <summary>
        /// Specifies that the continuation task should be scheduled only if its antecedent ran to
        /// completion. This option is not valid for multi-task continuations.
        /// </summary>
        OnlyOnRanToCompletion = NotOnFaulted | NotOnCanceled,
        /// <summary>
        /// Specifies that the continuation task should be scheduled only if its antecedent threw an
        /// unhandled exception. This option is not valid for multi-task continuations.
        /// </summary>
        OnlyOnFaulted = NotOnRanToCompletion | NotOnCanceled,
        /// <summary>
        /// Specifies that the continuation task should be scheduled only if its antecedent was canceled.
        /// This option is not valid for multi-task continuations.
        /// </summary>
        OnlyOnCanceled = NotOnRanToCompletion | NotOnFaulted,
        /// <summary>
        /// Specifies that the continuation task should be executed synchronously. With this option
        /// specified, the continuation will be run on the same thread that causes the antecedent task to
        /// transition into its final state. If the antecedent is already complete when the continuation is
        /// created, the continuation will run on the thread creating the continuation.  Only very
        /// short-running continuations should be executed synchronously.
        /// </summary>
        ExecuteSynchronously = 0x80000
    }

    // Special internal struct that we use to signify that we are not interested in
    // a Task<VoidTaskResult>'s result.
    internal readonly struct VoidTaskResult { }

    // Interface to which all completion actions must conform.
    // This interface allows us to combine functionality and reduce allocations.
    // For example, see Task.SetOnInvokeMres, and its use in Task.SpinThenBlockingWait().
    // This code:
    //      ManualResetEvent mres = new ManualResetEventSlim(false, 0);
    //      Action<Task> completionAction = delegate { mres.Set() ; };
    //      AddCompletionAction(completionAction);
    // gets replaced with this:
    //      SetOnInvokeMres mres = new SetOnInvokeMres();
    //      AddCompletionAction(mres);
    // For additional examples of where this is used, see internal classes Task.SignalOnInvokeCDE,
    // Task.WhenAllPromise, Task.WhenAllPromise<T>, TaskFactory.CompleteOnCountdownPromise,
    // TaskFactory.CompleteOnCountdownPromise<T>, and TaskFactory.CompleteOnInvokePromise.
    internal interface ITaskCompletionAction
    {
        /// <summary>Invoked to run the completion action.</summary>
        void Invoke(Task completingTask);

        /// <summary>
        /// Some completion actions are considered internal implementation details of tasks,
        /// using the continuation mechanism only for performance reasons.  Such actions perform
        /// known quantities and types of work, and can be invoked safely as a continuation even
        /// if the system wants to prevent arbitrary continuations from running synchronously.
        /// This should only return false for a limited set of implementations where a small amount
        /// of work is guaranteed to be performed, e.g. setting a ManualResetEventSlim.
        /// </summary>
        bool InvokeMayRunArbitraryCode { get; }
    }

    // This class encapsulates all "unwrap" logic, and also implements ITaskCompletionAction,
    // which minimizes the allocations needed for queuing it to its antecedent.  This
    // logic is used by both the Unwrap extension methods and the unwrap-style Task.Run methods.
    internal sealed class UnwrapPromise<TResult> : Task<TResult>, ITaskCompletionAction
    {
        // The possible states for our UnwrapPromise, used by Invoke() to determine which logic to execute
        private const byte STATE_WAITING_ON_OUTER_TASK = 0; // Invoke() means "process completed outer task"
        private const byte STATE_WAITING_ON_INNER_TASK = 1; // Invoke() means "process completed inner task"
        private const byte STATE_DONE = 2;                  // Invoke() means "something went wrong and we are hosed!"

        // Keep track of our state; defaults to STATE_WAITING_ON_OUTER_TASK
        private byte _state;

        // "Should we check for OperationCanceledExceptions on the outer task and interpret them as proxy cancellation?"
        // Unwrap() sets this to false, Run() sets it to true.
        private readonly bool _lookForOce;

        public UnwrapPromise(Task outerTask, bool lookForOce)
            : base((object?)null, outerTask.CreationOptions & TaskCreationOptions.AttachedToParent)
        {
            _lookForOce = lookForOce;

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationBegin(this.Id, "Task.Unwrap", 0);

            if (s_asyncDebuggingEnabled)
                AddToActiveTasks(this);

            // Link ourselves to the outer task.
            // If the outer task has already completed, take the fast path
            // of immediately transferring its results or processing the inner task.
            if (outerTask.IsCompleted)
            {
                ProcessCompletedOuterTask(outerTask);
            }
            else // Otherwise, process its completion asynchronously.
            {
                outerTask.AddCompletionAction(this);
            }
        }

        // For ITaskCompletionAction
        public void Invoke(Task completingTask)
        {
            // If we're ok to inline, process the task. Otherwise, we're too deep on the stack, and
            // we shouldn't run the continuation chain here, so queue a work item to call back here
            // to Invoke asynchronously.
            if (RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                InvokeCore(completingTask);
            }
            else
            {
                InvokeCoreAsync(completingTask);
            }
        }

        /// <summary>
        /// Processes the completed task. InvokeCore could be called twice:
        /// once for the outer task, once for the inner task.
        /// </summary>
        /// <param name="completingTask">The completing outer or inner task.</param>
        private void InvokeCore(Task completingTask)
        {
            switch (_state)
            {
                case STATE_WAITING_ON_OUTER_TASK:
                    ProcessCompletedOuterTask(completingTask);
                    // We bump the state inside of ProcessCompletedOuterTask because it can also be called from the constructor.
                    break;
                case STATE_WAITING_ON_INNER_TASK:
                    bool result = TrySetFromTask(completingTask, lookForOce: false);
                    _state = STATE_DONE; // bump the state
                    break;
                default:
                    break;
            }
        }

        // Calls InvokeCore asynchronously.
        private void InvokeCoreAsync(Task completingTask)
        {
            // Queue a call to Invoke.  If we're so deep on the stack that we're at risk of overflowing,
            // there's a high liklihood this thread is going to be doing lots more work before
            // returning to the thread pool (at the very least unwinding through thousands of
            // stack frames).  So we queue to the global queue.
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                // InvokeCore(completingTask);
                var tuple = (TupleSlim<UnwrapPromise<TResult>, Task>)state!;
                tuple.Item1.InvokeCore(tuple.Item2);
            }, new TupleSlim<UnwrapPromise<TResult>, Task>(this, completingTask));
        }

        /// <summary>Processes the outer task once it's completed.</summary>
        /// <param name="task">The now-completed outer task.</param>
        private void ProcessCompletedOuterTask(Task task)
        {
            // Bump our state before proceeding any further
            _state = STATE_WAITING_ON_INNER_TASK;

            switch (task.Status)
            {
                // If the outer task did not complete successfully, then record the
                // cancellation/fault information to tcs.Task.
                case TaskStatus.Canceled:
                case TaskStatus.Faulted:
                    bool result = TrySetFromTask(task, _lookForOce);
                    break;

                // Otherwise, process the inner task it returned.
                case TaskStatus.RanToCompletion:
                    ProcessInnerTask(task is Task<Task<TResult>> taskOfTaskOfTResult ? // it's either a Task<Task> or Task<Task<TResult>>
                        taskOfTaskOfTResult.Result : ((Task<Task>)task).Result);
                    break;
            }
        }

        /// <summary>Transfer the completion status from "task" to ourself.</summary>
        /// <param name="task">The source task whose results should be transferred to this.</param>
        /// <param name="lookForOce">Whether or not to look for OperationCanceledExceptions in task's exceptions if it faults.</param>
        /// <returns>true if the transfer was successful; otherwise, false.</returns>
        private bool TrySetFromTask(Task task, bool lookForOce)
        {
            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationRelation(this.Id, CausalityRelation.Join);

            bool result = false;
            switch (task.Status)
            {
                case TaskStatus.Canceled:
                    result = TrySetCanceled(task.CancellationToken, task.GetCancellationExceptionDispatchInfo());
                    break;

                case TaskStatus.Faulted:
                    List<ExceptionDispatchInfo> edis = task.GetExceptionDispatchInfos();
                    ExceptionDispatchInfo oceEdi;
                    if (lookForOce && edis.Count > 0 &&
                        (oceEdi = edis[0]) != null &&
                        oceEdi.SourceException is OperationCanceledException oce)
                    {
                        result = TrySetCanceled(oce.CancellationToken, oceEdi);
                    }
                    else
                    {
                        result = TrySetException(edis);
                    }
                    break;

                case TaskStatus.RanToCompletion:
                    if (TplEventSource.Log.IsEnabled())
                        TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);

                    if (s_asyncDebuggingEnabled)
                        RemoveFromActiveTasks(this);

                    result = TrySetResult(task is Task<TResult> taskTResult ? taskTResult.Result : default);
                    break;
            }
            return result;
        }

        /// <summary>
        /// Processes the inner task of a Task{Task} or Task{Task{TResult}},
        /// transferring the appropriate results to ourself.
        /// </summary>
        /// <param name="task">The inner task returned by the task provided by the user.</param>
        private void ProcessInnerTask(Task? task)
        {
            // If the inner task is null, the proxy should be canceled.
            if (task == null)
            {
                TrySetCanceled(default);
                _state = STATE_DONE; // ... and record that we are done
            }

            // Fast path for if the inner task is already completed
            else if (task.IsCompleted)
            {
                TrySetFromTask(task, lookForOce: false);
                _state = STATE_DONE; // ... and record that we are done
            }

            // The inner task exists but is not yet complete, so when it does complete,
            // take some action to set our completion state.
            else
            {
                task.AddCompletionAction(this);
                // We'll record that we are done when Invoke() is called.
            }
        }

        public bool InvokeMayRunArbitraryCode => true;
    }

    /// <summary>Provides an awaitable result of an asynchronous operation.</summary>
    /// <remarks>
    /// <see cref="ValueTask"/> instances are meant to be directly awaited.  To do more complicated operations with them, a <see cref="Task"/>
    /// should be extracted using <see cref="AsTask"/>.  Such operations might include caching a task instance to be awaited later,
    /// registering multiple continuations with a single task, awaiting the same task multiple times, and using combinators over
    /// multiple operations:
    /// <list type="bullet">
    /// <item>
    /// Once the result of a <see cref="ValueTask"/> instance has been retrieved, do not attempt to retrieve it again.
    /// <see cref="ValueTask"/> instances may be backed by <see cref="IValueTaskSource"/> instances that are reusable, and such
    /// instances may use the act of retrieving the instances result as a notification that the instance may now be reused for
    /// a different operation.  Attempting to then reuse that same <see cref="ValueTask"/> results in undefined behavior.
    /// </item>
    /// <item>
    /// Do not attempt to add multiple continuations to the same <see cref="ValueTask"/>.  While this might work if the
    /// <see cref="ValueTask"/> wraps a <code>T</code> or a <see cref="Task"/>, it may not work if the <see cref="ValueTask"/>
    /// was constructed from an <see cref="IValueTaskSource"/>.
    /// </item>
    /// <item>
    /// Some operations that return a <see cref="ValueTask"/> may invalidate it based on some subsequent operation being performed.
    /// Unless otherwise documented, assume that a <see cref="ValueTask"/> should be awaited prior to performing any additional operations
    /// on the instance from which it was retrieved.
    /// </item>
    /// </list>
    /// </remarks>
    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ValueTask : IEquatable<ValueTask>
    {
        /// <summary>A task canceled using `new CancellationToken(true)`. Lazily created only when first needed.</summary>
        private static volatile Task? s_canceledTask;

        /// <summary>null if representing a successful synchronous completion, otherwise a <see cref="Task"/> or a <see cref="IValueTaskSource"/>.</summary>
        internal readonly object? _obj;
        /// <summary>Opaque value passed through to the <see cref="IValueTaskSource"/>.</summary>
        internal readonly short _token;
        /// <summary>true to continue on the captured context; otherwise, false.</summary>
        /// <remarks>Stored in the <see cref="ValueTask"/> rather than in the configured awaiter to utilize otherwise padding space.</remarks>
        internal readonly bool _continueOnCapturedContext;

        // An instance created with the default ctor (a zero init'd struct) represents a synchronously, successfully completed operation.

        /// <summary>Initialize the <see cref="ValueTask"/> with a <see cref="Task"/> that represents the operation.</summary>
        /// <param name="task">The task.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Intrinsic]
        public ValueTask(Task task)
        {
            if (task == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.task);
            }

            _obj = task;

            _continueOnCapturedContext = true;
            _token = 0;
        }

        /// <summary>Initialize the <see cref="ValueTask"/> with a <see cref="IValueTaskSource"/> object that represents the operation.</summary>
        /// <param name="source">The source.</param>
        /// <param name="token">Opaque value passed through to the <see cref="IValueTaskSource"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask(IValueTaskSource source, short token)
        {
            if (source == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            _obj = source;
            _token = token;

            _continueOnCapturedContext = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueTask(object? obj, short token, bool continueOnCapturedContext)
        {
            _obj = obj;
            _token = token;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        /// <summary>Gets a task that has already completed successfully.</summary>
        public static ValueTask CompletedTask
        {
            [Intrinsic]
            get => default;
        }

        /// <summary>Creates a <see cref="ValueTask{TResult}"/> that's completed successfully with the specified result.</summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="result">The result to store into the completed task.</param>
        /// <returns>The successfully completed task.</returns>
        [Intrinsic]
        public static ValueTask<TResult> FromResult<TResult>(TResult result) =>
            new ValueTask<TResult>(result);

        /// <summary>Creates a <see cref="ValueTask"/> that has completed due to cancellation with the specified cancellation token.</summary>
        /// <param name="cancellationToken">The cancellation token with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        public static ValueTask FromCanceled(CancellationToken cancellationToken) =>
            new ValueTask(Task.FromCanceled(cancellationToken));

        /// <summary>Creates a <see cref="ValueTask{TResult}"/> that has completed due to cancellation with the specified cancellation token.</summary>
        /// <param name="cancellationToken">The cancellation token with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        public static ValueTask<TResult> FromCanceled<TResult>(CancellationToken cancellationToken) =>
            new ValueTask<TResult>(Task.FromCanceled<TResult>(cancellationToken));

        /// <summary>Creates a <see cref="ValueTask"/> that has completed with the specified exception.</summary>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The faulted task.</returns>
        public static ValueTask FromException(Exception exception) =>
            new ValueTask(Task.FromException(exception));

        /// <summary>Creates a <see cref="ValueTask{TResult}"/> that has completed with the specified exception.</summary>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The faulted task.</returns>
        public static ValueTask<TResult> FromException<TResult>(Exception exception) =>
            new ValueTask<TResult>(Task.FromException<TResult>(exception));

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode() => _obj?.GetHashCode() ?? 0;

        /// <summary>Returns a value indicating whether this value is equal to a specified <see cref="object"/>.</summary>
        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is ValueTask &&
            Equals((ValueTask)obj);

        /// <summary>Returns a value indicating whether this value is equal to a specified <see cref="ValueTask"/> value.</summary>
        public bool Equals(ValueTask other) => _obj == other._obj && _token == other._token;

        /// <summary>Returns a value indicating whether two <see cref="ValueTask"/> values are equal.</summary>
        public static bool operator ==(ValueTask left, ValueTask right) =>
            left.Equals(right);

        /// <summary>Returns a value indicating whether two <see cref="ValueTask"/> values are not equal.</summary>
        public static bool operator !=(ValueTask left, ValueTask right) =>
            !left.Equals(right);

        /// <summary>
        /// Gets a <see cref="Task"/> object to represent this ValueTask.
        /// </summary>
        /// <remarks>
        /// It will either return the wrapped task object if one exists, or it'll
        /// manufacture a new task object to represent the result.
        /// </remarks>
        [Intrinsic]
        public Task AsTask()
        {
            object? obj = _obj;
            Debug.Assert(obj == null || obj is Task || obj is IValueTaskSource);
            return
                obj == null ? Task.CompletedTask :
                obj as Task ??
                GetTaskForValueTaskSource(Unsafe.As<IValueTaskSource>(obj));
        }

        /// <summary>Gets a <see cref="ValueTask"/> that may be used at any point in the future.</summary>
        public ValueTask Preserve() => _obj == null ? this : new ValueTask(AsTask());

        /// <summary>Creates a <see cref="Task"/> to represent the <see cref="IValueTaskSource"/>.</summary>
        /// <remarks>
        /// The <see cref="IValueTaskSource"/> is passed in rather than reading and casting <see cref="_obj"/>
        /// so that the caller can pass in an object it's already validated.
        /// </remarks>
        private Task GetTaskForValueTaskSource(IValueTaskSource t)
        {
            ValueTaskSourceStatus status = t.GetStatus(_token);
            if (status != ValueTaskSourceStatus.Pending)
            {
                try
                {
                    // Propagate any exceptions that may have occurred, then return
                    // an already successfully completed task.
                    t.GetResult(_token);
                    return Task.CompletedTask;

                    // If status is Faulted or Canceled, GetResult should throw.  But
                    // we can't guarantee every implementation will do the "right thing".
                    // If it doesn't throw, we just treat that as success and ignore
                    // the status.
                }
                catch (Exception exc)
                {
                    if (status == ValueTaskSourceStatus.Canceled)
                    {
                        if (exc is OperationCanceledException oce)
                        {
                            var task = new Task();
                            task.TrySetCanceled(oce.CancellationToken, oce);
                            return task;
                        }

                        // Benign race condition to initialize cached task, as identity doesn't matter.
                        return s_canceledTask ??= Task.FromCanceled(new CancellationToken(canceled: true));
                    }
                    else
                    {
                        return Task.FromException(exc);
                    }
                }
            }

            return new ValueTaskSourceAsTask(t, _token);
        }

        /// <summary>Type used to create a <see cref="Task"/> to represent a <see cref="IValueTaskSource"/>.</summary>
        private sealed class ValueTaskSourceAsTask : Task
        {
            private static readonly Action<object?> s_completionAction = static state =>
            {
                if (state is not ValueTaskSourceAsTask vtst ||
                    vtst._source is not IValueTaskSource source)
                {
                    // This could only happen if the IValueTaskSource passed the wrong state
                    // or if this callback were invoked multiple times such that the state
                    // was previously nulled out.
                    ThrowUnexpectedStateForKnownCallback(state);
                    return;

                    static void ThrowUnexpectedStateForKnownCallback(object? state) =>
                        throw new ArgumentOutOfRangeException(
                            nameof(state),
                            state is ValueTaskSourceAsTask vsts ?
                                $"{nameof(ValueTaskSourceAsTask)}.{nameof(_source)} : {vsts._source}" :
                                $"{nameof(state)} : {state}",
                            SR.Argument_UnexpectedStateForKnownCallback);
                }

                vtst._source = null;
                ValueTaskSourceStatus status = source.GetStatus(vtst._token);
                try
                {
                    source.GetResult(vtst._token);
                    vtst.TrySetResult();
                }
                catch (Exception exc)
                {
                    if (status == ValueTaskSourceStatus.Canceled)
                    {
                        if (exc is OperationCanceledException oce)
                        {
                            vtst.TrySetCanceled(oce.CancellationToken, oce);
                        }
                        else
                        {
                            vtst.TrySetCanceled(new CancellationToken(true));
                        }
                    }
                    else
                    {
                        vtst.TrySetException(exc);
                    }
                }
            };

            /// <summary>The associated <see cref="IValueTaskSource"/>.</summary>
            private IValueTaskSource? _source;
            /// <summary>The token to pass through to operations on <see cref="_source"/></summary>
            private readonly short _token;

            internal ValueTaskSourceAsTask(IValueTaskSource source, short token)
            {
                _token = token;
                _source = source;
                source.OnCompleted(s_completionAction, this, token, ValueTaskSourceOnCompletedFlags.None);
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask"/> represents a completed operation.</summary>
        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return true;
                }

                if (obj is Task t)
                {
                    return t.IsCompleted;
                }

                return Unsafe.As<IValueTaskSource>(obj).GetStatus(_token) != ValueTaskSourceStatus.Pending;
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask"/> represents a successfully completed operation.</summary>
        public bool IsCompletedSuccessfully
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return true;
                }

                if (obj is Task t)
                {
                    return t.IsCompletedSuccessfully;
                }

                return Unsafe.As<IValueTaskSource>(obj).GetStatus(_token) == ValueTaskSourceStatus.Succeeded;
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask"/> represents a failed operation.</summary>
        public bool IsFaulted
        {
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return false;
                }

                if (obj is Task t)
                {
                    return t.IsFaulted;
                }

                return Unsafe.As<IValueTaskSource>(obj).GetStatus(_token) == ValueTaskSourceStatus.Faulted;
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask"/> represents a canceled operation.</summary>
        /// <remarks>
        /// If the <see cref="ValueTask"/> is backed by a result or by a <see cref="IValueTaskSource"/>,
        /// this will always return false.  If it's backed by a <see cref="Task"/>, it'll return the
        /// value of the task's <see cref="Task.IsCanceled"/> property.
        /// </remarks>
        public bool IsCanceled
        {
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return false;
                }

                if (obj is Task t)
                {
                    return t.IsCanceled;
                }

                return Unsafe.As<IValueTaskSource>(obj).GetStatus(_token) == ValueTaskSourceStatus.Canceled;
            }
        }

        /// <summary>Throws the exception that caused the <see cref="ValueTask"/> to fail.  If it completed successfully, nothing is thrown.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ThrowIfCompletedUnsuccessfully()
        {
            object? obj = _obj;

            if (obj != null)
            {
                if (obj is Task t)
                {
                    TaskAwaiter.ValidateEnd(t);
                }
                else
                {
                    Unsafe.As<IValueTaskSource>(obj).GetResult(_token);
                }
            }
        }

        /// <summary>
        /// Transfers the <see cref="ValueTask{TResult}"/> to a <see cref="ValueTask"/> instance.
        ///
        /// The <see cref="ValueTask{TResult}"/> should not be used after calling this method.
        /// </summary>
        internal static ValueTask DangerousCreateFromTypedValueTask<TResult>(ValueTask<TResult> valueTask)
        {
            return new ValueTask(valueTask._obj, valueTask._token, valueTask._continueOnCapturedContext);
        }

        /// <summary>Gets an awaiter for this <see cref="ValueTask"/>.</summary>
        public ValueTaskAwaiter GetAwaiter() => new ValueTaskAwaiter(in this);

        /// <summary>Configures an awaiter for this <see cref="ValueTask"/>.</summary>
        /// <param name="continueOnCapturedContext">
        /// true to attempt to marshal the continuation back to the captured context; otherwise, false.
        /// </param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
            new ConfiguredValueTaskAwaitable(new ValueTask(_obj, _token, continueOnCapturedContext));
    }

    /// <summary>Provides a value type that can represent a synchronously available value or a task object.</summary>
    /// <typeparam name="TResult">Specifies the type of the result.</typeparam>
    /// <remarks>
    /// <see cref="ValueTask{TResult}"/> instances are meant to be directly awaited.  To do more complicated operations with them, a <see cref="Task{TResult}"/>
    /// should be extracted using <see cref="AsTask"/>.  Such operations might include caching a task instance to be awaited later,
    /// registering multiple continuations with a single task, awaiting the same task multiple times, and using combinators over
    /// multiple operations:
    /// <list type="bullet">
    /// <item>
    /// Once the result of a <see cref="ValueTask{TResult}"/> instance has been retrieved, do not attempt to retrieve it again.
    /// <see cref="ValueTask{TResult}"/> instances may be backed by <see cref="IValueTaskSource{TResult}"/> instances that are reusable, and such
    /// instances may use the act of retrieving the instances result as a notification that the instance may now be reused for
    /// a different operation.  Attempting to then reuse that same <see cref="ValueTask{TResult}"/> results in undefined behavior.
    /// </item>
    /// <item>
    /// Do not attempt to add multiple continuations to the same <see cref="ValueTask{TResult}"/>.  While this might work if the
    /// <see cref="ValueTask{TResult}"/> wraps a <code>T</code> or a <see cref="Task{TResult}"/>, it may not work if the <see cref="Task{TResult}"/>
    /// was constructed from an <see cref="IValueTaskSource{TResult}"/>.
    /// </item>
    /// <item>
    /// Some operations that return a <see cref="ValueTask{TResult}"/> may invalidate it based on some subsequent operation being performed.
    /// Unless otherwise documented, assume that a <see cref="ValueTask{TResult}"/> should be awaited prior to performing any additional operations
    /// on the instance from which it was retrieved.
    /// </item>
    /// </list>
    /// </remarks>
    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder<>))]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ValueTask<TResult> : IEquatable<ValueTask<TResult>>
    {
        /// <summary>A task canceled using `new CancellationToken(true)`. Lazily created only when first needed.</summary>
        private static volatile Task<TResult>? s_canceledTask;
        /// <summary>null if <see cref="_result"/> has the result, otherwise a <see cref="Task{TResult}"/> or a <see cref="IValueTaskSource{TResult}"/>.</summary>
        internal readonly object? _obj;
        /// <summary>The result to be used if the operation completed successfully synchronously.</summary>
        internal readonly TResult? _result;
        /// <summary>Opaque value passed through to the <see cref="IValueTaskSource{TResult}"/>.</summary>
        internal readonly short _token;
        /// <summary>true to continue on the captured context; otherwise, false.</summary>
        /// <remarks>Stored in the <see cref="ValueTask{TResult}"/> rather than in the configured awaiter to utilize otherwise padding space.</remarks>
        internal readonly bool _continueOnCapturedContext;

        // An instance created with the default ctor (a zero init'd struct) represents a synchronously, successfully completed operation
        // with a result of default(TResult).

        /// <summary>Initialize the <see cref="ValueTask{TResult}"/> with a <typeparamref name="TResult"/> result value.</summary>
        /// <param name="result">The result.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Intrinsic]
        public ValueTask(TResult result)
        {
            _result = result;

            _obj = null;
            _continueOnCapturedContext = true;
            _token = 0;
        }

        /// <summary>Initialize the <see cref="ValueTask{TResult}"/> with a <see cref="Task{TResult}"/> that represents the operation.</summary>
        /// <param name="task">The task.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Intrinsic]
        public ValueTask(Task<TResult> task)
        {
            if (task == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.task);
            }

            _obj = task;

            _result = default;
            _continueOnCapturedContext = true;
            _token = 0;
        }

        /// <summary>Initialize the <see cref="ValueTask{TResult}"/> with a <see cref="IValueTaskSource{TResult}"/> object that represents the operation.</summary>
        /// <param name="source">The source.</param>
        /// <param name="token">Opaque value passed through to the <see cref="IValueTaskSource"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask(IValueTaskSource<TResult> source, short token)
        {
            if (source == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            _obj = source;
            _token = token;

            _result = default;
            _continueOnCapturedContext = true;
        }

        /// <summary>Non-verified initialization of the struct to the specified values.</summary>
        /// <param name="obj">The object.</param>
        /// <param name="result">The result.</param>
        /// <param name="token">The token.</param>
        /// <param name="continueOnCapturedContext">true to continue on captured context; otherwise, false.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueTask(object? obj, TResult? result, short token, bool continueOnCapturedContext)
        {
            _obj = obj;
            _result = result;
            _token = token;
            _continueOnCapturedContext = continueOnCapturedContext;
        }


        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode() =>
            _obj != null ? _obj.GetHashCode() :
            _result != null ? _result.GetHashCode() :
            0;

        /// <summary>Returns a value indicating whether this value is equal to a specified <see cref="object"/>.</summary>
        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is ValueTask<TResult> &&
            Equals((ValueTask<TResult>)obj);

        /// <summary>Returns a value indicating whether this value is equal to a specified <see cref="ValueTask{TResult}"/> value.</summary>
        public bool Equals(ValueTask<TResult> other) =>
            _obj != null || other._obj != null ?
                _obj == other._obj && _token == other._token :
                EqualityComparer<TResult>.Default.Equals(_result, other._result);

        /// <summary>Returns a value indicating whether two <see cref="ValueTask{TResult}"/> values are equal.</summary>
        public static bool operator ==(ValueTask<TResult> left, ValueTask<TResult> right) =>
            left.Equals(right);

        /// <summary>Returns a value indicating whether two <see cref="ValueTask{TResult}"/> values are not equal.</summary>
        public static bool operator !=(ValueTask<TResult> left, ValueTask<TResult> right) =>
            !left.Equals(right);

        /// <summary>
        /// Gets a <see cref="Task{TResult}"/> object to represent this ValueTask.
        /// </summary>
        /// <remarks>
        /// It will either return the wrapped task object if one exists, or it'll
        /// manufacture a new task object to represent the result.
        /// </remarks>
        [Intrinsic]
        public Task<TResult> AsTask()
        {
            object? obj = _obj;
            Debug.Assert(obj == null || obj is Task<TResult> || obj is IValueTaskSource<TResult>);

            if (obj == null)
            {
                return Task.FromResult(_result!);
            }

            if (obj is Task<TResult> t)
            {
                return t;
            }

            return GetTaskForValueTaskSource(Unsafe.As<IValueTaskSource<TResult>>(obj));
        }

        /// <summary>Gets a <see cref="ValueTask{TResult}"/> that may be used at any point in the future.</summary>
        public ValueTask<TResult> Preserve() => _obj == null ? this : new ValueTask<TResult>(AsTask());

        /// <summary>Creates a <see cref="Task{TResult}"/> to represent the <see cref="IValueTaskSource{TResult}"/>.</summary>
        /// <remarks>
        /// The <see cref="IValueTaskSource{TResult}"/> is passed in rather than reading and casting <see cref="_obj"/>
        /// so that the caller can pass in an object it's already validated.
        /// </remarks>
        private Task<TResult> GetTaskForValueTaskSource(IValueTaskSource<TResult> t)
        {
            ValueTaskSourceStatus status = t.GetStatus(_token);
            if (status != ValueTaskSourceStatus.Pending)
            {
                try
                {
                    // Get the result of the operation and return a task for it.
                    // If any exception occurred, propagate it
                    return Task.FromResult(t.GetResult(_token));

                    // If status is Faulted or Canceled, GetResult should throw.  But
                    // we can't guarantee every implementation will do the "right thing".
                    // If it doesn't throw, we just treat that as success and ignore
                    // the status.
                }
                catch (Exception exc)
                {
                    if (status == ValueTaskSourceStatus.Canceled)
                    {
                        if (exc is OperationCanceledException oce)
                        {
                            var task = new Task<TResult>();
                            task.TrySetCanceled(oce.CancellationToken, oce);
                            return task;
                        }

                        // Benign race condition to initialize cached task, as identity doesn't matter.
                        return s_canceledTask ??= Task.FromCanceled<TResult>(new CancellationToken(true));
                    }
                    else
                    {
                        return Task.FromException<TResult>(exc);
                    }
                }
            }

            return new ValueTaskSourceAsTask(t, _token);
        }

        /// <summary>Type used to create a <see cref="Task{TResult}"/> to represent a <see cref="IValueTaskSource{TResult}"/>.</summary>
        private sealed class ValueTaskSourceAsTask : Task<TResult>
        {
            private static readonly Action<object?> s_completionAction = static state =>
            {
                if (state is not ValueTaskSourceAsTask vtst ||
                    vtst._source is not IValueTaskSource<TResult> source)
                {
                    // This could only happen if the IValueTaskSource<TResult> passed the wrong state
                    // or if this callback were invoked multiple times such that the state
                    // was previously nulled out.
                    ThrowUnexpectedStateForKnownCallback(state);
                    return;

                    static void ThrowUnexpectedStateForKnownCallback(object? state) =>
                        throw new ArgumentOutOfRangeException(
                            nameof(state),
                            state is ValueTaskSourceAsTask vsts ?
                                $"{nameof(ValueTaskSourceAsTask)}.{nameof(_source)} : {vsts._source}" :
                                $"{nameof(state)} : {state}",
                            SR.Argument_UnexpectedStateForKnownCallback);
                }

                vtst._source = null;
                ValueTaskSourceStatus status = source.GetStatus(vtst._token);
                try
                {
                    vtst.TrySetResult(source.GetResult(vtst._token));
                }
                catch (Exception exc)
                {
                    if (status == ValueTaskSourceStatus.Canceled)
                    {
                        if (exc is OperationCanceledException oce)
                        {
                            vtst.TrySetCanceled(oce.CancellationToken, oce);
                        }
                        else
                        {
                            vtst.TrySetCanceled(new CancellationToken(true));
                        }
                    }
                    else
                    {
                        vtst.TrySetException(exc);
                    }
                }
            };

            /// <summary>The associated <see cref="IValueTaskSource"/>.</summary>
            private IValueTaskSource<TResult>? _source;
            /// <summary>The token to pass through to operations on <see cref="_source"/></summary>
            private readonly short _token;

            public ValueTaskSourceAsTask(IValueTaskSource<TResult> source, short token)
            {
                _source = source;
                _token = token;
                source.OnCompleted(s_completionAction, this, token, ValueTaskSourceOnCompletedFlags.None);
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask{TResult}"/> represents a completed operation.</summary>
        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return true;
                }

                if (obj is Task<TResult> t)
                {
                    return t.IsCompleted;
                }

                return Unsafe.As<IValueTaskSource<TResult>>(obj).GetStatus(_token) != ValueTaskSourceStatus.Pending;
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask{TResult}"/> represents a successfully completed operation.</summary>
        public bool IsCompletedSuccessfully
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return true;
                }

                if (obj is Task<TResult> t)
                {
                    return t.IsCompletedSuccessfully;
                }

                return Unsafe.As<IValueTaskSource<TResult>>(obj).GetStatus(_token) == ValueTaskSourceStatus.Succeeded;
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask{TResult}"/> represents a failed operation.</summary>
        public bool IsFaulted
        {
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return false;
                }

                if (obj is Task<TResult> t)
                {
                    return t.IsFaulted;
                }

                return Unsafe.As<IValueTaskSource<TResult>>(obj).GetStatus(_token) == ValueTaskSourceStatus.Faulted;
            }
        }

        /// <summary>Gets whether the <see cref="ValueTask{TResult}"/> represents a canceled operation.</summary>
        /// <remarks>
        /// If the <see cref="ValueTask{TResult}"/> is backed by a result or by a <see cref="IValueTaskSource{TResult}"/>,
        /// this will always return false.  If it's backed by a <see cref="Task"/>, it'll return the
        /// value of the task's <see cref="Task.IsCanceled"/> property.
        /// </remarks>
        public bool IsCanceled
        {
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return false;
                }

                if (obj is Task<TResult> t)
                {
                    return t.IsCanceled;
                }

                return Unsafe.As<IValueTaskSource<TResult>>(obj).GetStatus(_token) == ValueTaskSourceStatus.Canceled;
            }
        }

        /// <summary>Gets the result.</summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)] // prevent debugger evaluation from invalidating an underling IValueTaskSource<T>
        public TResult Result
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                object? obj = _obj;

                if (obj == null)
                {
                    return _result!;
                }

                if (obj is Task<TResult> t)
                {
                    TaskAwaiter.ValidateEnd(t);
                    return t.ResultOnSuccess;
                }

                return Unsafe.As<IValueTaskSource<TResult>>(obj).GetResult(_token);
            }
        }

        /// <summary>Gets an awaiter for this <see cref="ValueTask{TResult}"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTaskAwaiter<TResult> GetAwaiter() => new ValueTaskAwaiter<TResult>(in this);

        /// <summary>Configures an awaiter for this <see cref="ValueTask{TResult}"/>.</summary>
        /// <param name="continueOnCapturedContext">
        /// true to attempt to marshal the continuation back to the captured context; otherwise, false.
        /// </param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ConfiguredValueTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) =>
            new ConfiguredValueTaskAwaitable<TResult>(new ValueTask<TResult>(_obj, _result, _token, continueOnCapturedContext));

        /// <summary>Gets a string-representation of this <see cref="ValueTask{TResult}"/>.</summary>
        public override string? ToString()
        {
            if (IsCompletedSuccessfully)
            {
                Debugger.NotifyOfCrossThreadDependency(); // prevent debugger evaluation from invalidating an underling IValueTaskSource<T> unless forced

                TResult result = Result;
                if (result != null)
                {
                    return result.ToString();
                }
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Represents an abstract scheduler for tasks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TaskScheduler">TaskScheduler</see> acts as the extension point for all
    /// pluggable scheduling logic.  This includes mechanisms such as how to schedule a task for execution, and
    /// how scheduled tasks should be exposed to debuggers.
    /// </para>
    /// <para>
    /// All members of the abstract <see cref="TaskScheduler"/> type are thread-safe
    /// and may be used from multiple threads concurrently.
    /// </para>
    /// </remarks>
    public abstract class TaskScheduler
    {
        ////////////////////////////////////////////////////////////
        //
        // User Provided Methods and Properties
        //

        /// <summary>
        /// Queues a <see cref="Task">Task</see> to the scheduler.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A class derived from <see cref="TaskScheduler">TaskScheduler</see>
        /// implements this method to accept tasks being scheduled on the scheduler.
        /// A typical implementation would store the task in an internal data structure, which would
        /// be serviced by threads that would execute those tasks at some time in the future.
        /// </para>
        /// <para>
        /// This method is only meant to be called by the .NET Framework and
        /// should not be called directly by the derived class. This is necessary
        /// for maintaining the consistency of the system.
        /// </para>
        /// </remarks>
        /// <param name="task">The <see cref="Task">Task</see> to be queued.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="task"/> argument is null.</exception>
        protected internal abstract void QueueTask(Task task);

        /// <summary>
        /// Determines whether the provided <see cref="Task">Task</see>
        /// can be executed synchronously in this call, and if it can, executes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A class derived from <see cref="TaskScheduler">TaskScheduler</see> implements this function to
        /// support inline execution of a task on a thread that initiates a wait on that task object. Inline
        /// execution is optional, and the request may be rejected by returning false. However, better
        /// scalability typically results the more tasks that can be inlined, and in fact a scheduler that
        /// inlines too little may be prone to deadlocks. A proper implementation should ensure that a
        /// request executing under the policies guaranteed by the scheduler can successfully inline. For
        /// example, if a scheduler uses a dedicated thread to execute tasks, any inlining requests from that
        /// thread should succeed.
        /// </para>
        /// <para>
        /// If a scheduler decides to perform the inline execution, it should do so by calling to the base
        /// TaskScheduler's
        /// <see cref="TryExecuteTask">TryExecuteTask</see> method with the provided task object, propagating
        /// the return value. It may also be appropriate for the scheduler to remove an inlined task from its
        /// internal data structures if it decides to honor the inlining request. Note, however, that under
        /// some circumstances a scheduler may be asked to inline a task that was not previously provided to
        /// it with the <see cref="QueueTask"/> method.
        /// </para>
        /// <para>
        /// The derived scheduler is responsible for making sure that the calling thread is suitable for
        /// executing the given task as far as its own scheduling and execution policies are concerned.
        /// </para>
        /// </remarks>
        /// <param name="task">The <see cref="Task">Task</see> to be
        /// executed.</param>
        /// <param name="taskWasPreviouslyQueued">A Boolean denoting whether or not task has previously been
        /// queued. If this parameter is True, then the task may have been previously queued (scheduled); if
        /// False, then the task is known not to have been queued, and this call is being made in order to
        /// execute the task inline without queueing it.</param>
        /// <returns>A Boolean value indicating whether the task was executed inline.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="task"/> argument is
        /// null.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="task"/> was already
        /// executed.</exception>
        protected abstract bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued);

        /// <summary>
        /// Generates an enumerable of <see cref="Task">Task</see> instances
        /// currently queued to the scheduler waiting to be executed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A class derived from <see cref="TaskScheduler"/> implements this method in order to support
        /// integration with debuggers. This method will only be invoked by the .NET Framework when the
        /// debugger requests access to the data. The enumerable returned will be traversed by debugging
        /// utilities to access the tasks currently queued to this scheduler, enabling the debugger to
        /// provide a representation of this information in the user interface.
        /// </para>
        /// <para>
        /// It is important to note that, when this method is called, all other threads in the process will
        /// be frozen. Therefore, it's important to avoid synchronization with other threads that may lead to
        /// blocking. If synchronization is necessary, the method should prefer to throw a <see
        /// cref="NotSupportedException"/>
        /// than to block, which could cause a debugger to experience delays. Additionally, this method and
        /// the enumerable returned must not modify any globally visible state.
        /// </para>
        /// <para>
        /// The returned enumerable should never be null. If there are currently no queued tasks, an empty
        /// enumerable should be returned instead.
        /// </para>
        /// <para>
        /// For developers implementing a custom debugger, this method shouldn't be called directly, but
        /// rather this functionality should be accessed through the internal wrapper method
        /// GetScheduledTasksForDebugger:
        /// <c>internal Task[] GetScheduledTasksForDebugger()</c>. This method returns an array of tasks,
        /// rather than an enumerable. In order to retrieve a list of active schedulers, a debugger may use
        /// another internal method: <c>internal static TaskScheduler[] GetTaskSchedulersForDebugger()</c>.
        /// This static method returns an array of all active TaskScheduler instances.
        /// GetScheduledTasksForDebugger then may be used on each of these scheduler instances to retrieve
        /// the list of scheduled tasks for each.
        /// </para>
        /// </remarks>
        /// <returns>An enumerable that allows traversal of tasks currently queued to this scheduler.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// This scheduler is unable to generate a list of queued tasks at this time.
        /// </exception>
        protected abstract IEnumerable<Task>? GetScheduledTasks();

        /// <summary>
        /// Indicates the maximum concurrency level this
        /// <see cref="TaskScheduler"/>  is able to support.
        /// </summary>
        public virtual int MaximumConcurrencyLevel => int.MaxValue;

        ////////////////////////////////////////////////////////////
        //
        // Internal overridable methods
        //

        /// <summary>
        /// Attempts to dequeue a <see cref="Task">Task</see> that was previously queued to
        /// this scheduler.
        /// </summary>
        /// <param name="task">The <see cref="Task">Task</see> to be dequeued.</param>
        /// <returns>A Boolean denoting whether the <paramref name="task"/> argument was successfully dequeued.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="task"/> argument is null.</exception>
        protected internal virtual bool TryDequeue(Task task)
        {
            return false;
        }

        /// <summary>
        /// Notifies the scheduler that a work item has made progress.
        /// </summary>
        internal virtual void NotifyWorkItemProgress()
        {
        }

        ////////////////////////////////////////////////////////////
        //
        // Member variables
        //

        // static counter used to generate unique TaskScheduler IDs
        internal static int s_taskSchedulerIdCounter;

        // this TaskScheduler's unique ID
        private volatile int m_taskSchedulerId;

        ////////////////////////////////////////////////////////////
        //
        // Constructors and public properties
        //

        /// <summary>
        /// Initializes the <see cref="TaskScheduler"/>.
        /// </summary>
        protected TaskScheduler()
        {

        }

        private static ConditionalWeakTable<TaskScheduler, object> s_activeTaskSchedulers;

        private static readonly TaskScheduler s_defaultTaskScheduler = new ThreadPoolTaskScheduler();

        public static TaskScheduler Default => s_defaultTaskScheduler;

        public static TaskScheduler Current => InternalCurrent ?? Default;

        internal static TaskScheduler? InternalCurrent
        {
            get
            {
                Task internalCurrent = Task.InternalCurrent;
                if (internalCurrent is null || (internalCurrent.CreationOptions & TaskCreationOptions.HideScheduler) != TaskCreationOptions.None)
                {
                    return null;
                }
                return internalCurrent.ExecutingTaskScheduler;
            }
        }

        public int Id
        {
            get
            {
                int taskSchedulerId = m_taskSchedulerId;
                if (taskSchedulerId == 0)
                {
                    int num;
                    do
                    {
                        num = Interlocked.Increment(ref s_taskSchedulerIdCounter);
                    }
                    while (num == 0);
                    Interlocked.CompareExchange(ref m_taskSchedulerId, num, 0);
                    taskSchedulerId = m_taskSchedulerId;
                }
                return taskSchedulerId;
            }
        }

        public static event EventHandler<UnobservedTaskExceptionEventArgs>? UnobservedTaskException;

        internal bool TryRunInline(Task task, bool taskWasPreviouslyQueued)
        {
            TaskScheduler executingTaskScheduler = task.ExecutingTaskScheduler;
            if (executingTaskScheduler != this && executingTaskScheduler is not null)
            {
                return executingTaskScheduler.TryRunInline(task, taskWasPreviouslyQueued);
            }
            if (executingTaskScheduler is null || (object)task.m_action is null || task.IsDelegateInvoked || task.IsCanceled || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                return false;
            }
            if (TplEventSource.Log.IsEnabled())
            {
                task.FireTaskScheduledIfNeeded(this);
            }
            bool num = TryExecuteTaskInline(task, taskWasPreviouslyQueued);
            if (num && !task.IsDelegateInvoked && !task.IsCanceled)
            {
                throw new InvalidOperationException(SR.TaskScheduler_InconsistentStateAfterTryExecuteTaskInline);
            }
            return num;
        }

        internal void InternalQueueTask(Task task)
        {
            if (TplEventSource.Log.IsEnabled())
            {
                task.FireTaskScheduledIfNeeded(this);
            }
            QueueTask(task);
        }

        private void AddToActiveTaskSchedulers()
        {
            ConditionalWeakTable<TaskScheduler, object> conditionalWeakTable = s_activeTaskSchedulers;
            if (conditionalWeakTable is null)
            {
                Interlocked.CompareExchange(ref s_activeTaskSchedulers, new ConditionalWeakTable<TaskScheduler, object>(), null);
                conditionalWeakTable = s_activeTaskSchedulers;
            }
            conditionalWeakTable.Add(this, null);
        }

        public static TaskScheduler FromCurrentSynchronizationContext()
        {
            return new SynchronizationContextTaskScheduler();
        }

        protected bool TryExecuteTask(Task task)
        {
            if (task.ExecutingTaskScheduler != this)
            {
                throw new InvalidOperationException(SR.TaskScheduler_ExecuteTask_WrongTaskScheduler);
            }
            return task.ExecuteEntry();
        }

        internal static void PublishUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs ueea)
        {
            UnobservedTaskException?.Invoke(sender, ueea);
        }

        internal Task[] GetScheduledTasksForDebugger()
        {
            IEnumerable<Task> scheduledTasks = GetScheduledTasks();
            if (scheduledTasks is null)
            {
                return null;
            }
            Task[] array = scheduledTasks as Task[];
            if (array is null)
            {
                array = new List<Task>(scheduledTasks).ToArray();
            }
            Task[] array2 = array;
            for (int i = 0; i < array2.Length; i++)
            {
                _ = array2[i].Id;
            }
            return array;
        }

        internal static TaskScheduler[] GetTaskSchedulersForDebugger()
        {
            if (s_activeTaskSchedulers is null)
            {
                return new TaskScheduler[1] { s_defaultTaskScheduler };
            }
            List<TaskScheduler> list = new List<TaskScheduler>();
            foreach (KeyValuePair<TaskScheduler, object> item in (IEnumerable<KeyValuePair<TaskScheduler, object>>)s_activeTaskSchedulers)
            {
                list.Add(item.Key);
            }
            if (!list.Contains(s_defaultTaskScheduler))
            {
                list.Add(s_defaultTaskScheduler);
            }
            TaskScheduler[] array = list.ToArray();
            TaskScheduler[] array2 = array;
            for (int i = 0; i < array2.Length; i++)
            {
                _ = array2[i].Id;
            }
            return array;
        }
    }


    /// <summary>
    /// Provides support for creating and scheduling
    /// <see cref="Task{TResult}">Task{TResult}</see> objects.
    /// </summary>
    /// <typeparam name="TResult">The type of the results that are available though
    /// the <see cref="Task{TResult}">Task{TResult}</see> objects that are associated with
    /// the methods in this class.</typeparam>
    /// <remarks>
    /// <para>
    /// There are many common patterns for which tasks are relevant. The <see cref="TaskFactory{TResult}"/>
    /// class encodes some of these patterns into methods that pick up default settings, which are
    /// configurable through its constructors.
    /// </para>
    /// <para>
    /// A default instance of <see cref="TaskFactory{TResult}"/> is available through the
    /// <see cref="Task{TResult}.Factory">Task{TResult}.Factory</see> property.
    /// </para>
    /// </remarks>
    public class TaskFactory<TResult>
    {
        // Member variables, DefaultScheduler, other properties and ctors
        // copied right out of TaskFactory...  Lots of duplication here...
        // Should we be thinking about a TaskFactoryBase class?

        // member variables
        private readonly CancellationToken m_defaultCancellationToken;
        private readonly TaskScheduler? m_defaultScheduler;
        private readonly TaskCreationOptions m_defaultCreationOptions;
        private readonly TaskContinuationOptions m_defaultContinuationOptions;

        private TaskScheduler DefaultScheduler => m_defaultScheduler ?? TaskScheduler.Current;

        // sister method to above property -- avoids a TLS lookup
        private TaskScheduler GetDefaultScheduler(Task? currTask)
        {
            if (m_defaultScheduler != null) return m_defaultScheduler;
            else if ((currTask != null)
                && ((currTask.CreationOptions & TaskCreationOptions.HideScheduler) == 0)
                )
                return currTask.ExecutingTaskScheduler!; // a "current" task must be executing, which means it must have a scheduler
            else return TaskScheduler.Default;
        }

        /* Constructors */

        /// <summary>
        /// Initializes a <see cref="TaskFactory{TResult}"/> instance with the default configuration.
        /// </summary>
        /// <remarks>
        /// This constructor creates a <see cref="TaskFactory{TResult}"/> instance with a default configuration. The
        /// <see cref="TaskCreationOptions"/> property is initialized to
        /// <see cref="TaskCreationOptions.None">TaskCreationOptions.None</see>, the
        /// <see cref="TaskContinuationOptions"/> property is initialized to <see
        /// cref="TaskContinuationOptions.None">TaskContinuationOptions.None</see>,
        /// and the <see cref="TaskScheduler">TaskScheduler</see> property is
        /// initialized to the current scheduler (see <see
        /// cref="TaskScheduler.Current">TaskScheduler.Current</see>).
        /// </remarks>
        public TaskFactory()
        {
        }

        /// <summary>
        /// Initializes a <see cref="TaskFactory{TResult}"/> instance with the default configuration.
        /// </summary>
        /// <param name="cancellationToken">The default <see cref="CancellationToken"/> that will be assigned
        /// to tasks created by this <see cref="TaskFactory"/> unless another CancellationToken is explicitly specified
        /// while calling the factory methods.</param>
        /// <remarks>
        /// This constructor creates a <see cref="TaskFactory{TResult}"/> instance with a default configuration. The
        /// <see cref="TaskCreationOptions"/> property is initialized to
        /// <see cref="TaskCreationOptions.None">TaskCreationOptions.None</see>, the
        /// <see cref="TaskContinuationOptions"/> property is initialized to <see
        /// cref="TaskContinuationOptions.None">TaskContinuationOptions.None</see>,
        /// and the <see cref="TaskScheduler">TaskScheduler</see> property is
        /// initialized to the current scheduler (see <see
        /// cref="TaskScheduler.Current">TaskScheduler.Current</see>).
        /// </remarks>
        public TaskFactory(CancellationToken cancellationToken)
        {
            m_defaultCancellationToken = cancellationToken;
        }

        /// <summary>
        /// Initializes a <see cref="TaskFactory{TResult}"/> instance with the specified configuration.
        /// </summary>
        /// <param name="scheduler">
        /// The <see cref="TaskScheduler">
        /// TaskScheduler</see> to use to schedule any tasks created with this TaskFactory{TResult}. A null value
        /// indicates that the current TaskScheduler should be used.
        /// </param>
        /// <remarks>
        /// With this constructor, the
        /// <see cref="TaskCreationOptions"/> property is initialized to
        /// <see cref="TaskCreationOptions.None">TaskCreationOptions.None</see>, the
        /// <see cref="TaskContinuationOptions"/> property is initialized to <see
        /// cref="TaskContinuationOptions.None">TaskContinuationOptions.None</see>,
        /// and the <see cref="TaskScheduler">TaskScheduler</see> property is
        /// initialized to <paramref name="scheduler"/>, unless it's null, in which case the property is
        /// initialized to the current scheduler (see <see
        /// cref="TaskScheduler.Current">TaskScheduler.Current</see>).
        /// </remarks>
        public TaskFactory(TaskScheduler? scheduler) // null means to use TaskScheduler.Current
        {
            m_defaultScheduler = scheduler;
        }

        /// <summary>
        /// Initializes a <see cref="TaskFactory{TResult}"/> instance with the specified configuration.
        /// </summary>
        /// <param name="creationOptions">
        /// The default <see cref="TaskCreationOptions">
        /// TaskCreationOptions</see> to use when creating tasks with this TaskFactory{TResult}.
        /// </param>
        /// <param name="continuationOptions">
        /// The default <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> to use when creating continuation tasks with this TaskFactory{TResult}.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument or the <paramref name="continuationOptions"/>
        /// argument specifies an invalid value.
        /// </exception>
        /// <remarks>
        /// With this constructor, the
        /// <see cref="TaskCreationOptions"/> property is initialized to <paramref name="creationOptions"/>,
        /// the
        /// <see cref="TaskContinuationOptions"/> property is initialized to <paramref
        /// name="continuationOptions"/>, and the <see
        /// cref="TaskScheduler">TaskScheduler</see> property is initialized to the
        /// current scheduler (see <see
        /// cref="TaskScheduler.Current">TaskScheduler.Current</see>).
        /// </remarks>
        public TaskFactory(TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions)
        {
            TaskFactory.CheckMultiTaskContinuationOptions(continuationOptions);
            TaskFactory.CheckCreationOptions(creationOptions);

            m_defaultCreationOptions = creationOptions;
            m_defaultContinuationOptions = continuationOptions;
        }

        /// <summary>
        /// Initializes a <see cref="TaskFactory{TResult}"/> instance with the specified configuration.
        /// </summary>
        /// <param name="cancellationToken">The default <see cref="CancellationToken"/> that will be assigned
        /// to tasks created by this <see cref="TaskFactory"/> unless another CancellationToken is explicitly specified
        /// while calling the factory methods.</param>
        /// <param name="creationOptions">
        /// The default <see cref="TaskCreationOptions">
        /// TaskCreationOptions</see> to use when creating tasks with this TaskFactory{TResult}.
        /// </param>
        /// <param name="continuationOptions">
        /// The default <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> to use when creating continuation tasks with this TaskFactory{TResult}.
        /// </param>
        /// <param name="scheduler">
        /// The default <see cref="TaskScheduler">
        /// TaskScheduler</see> to use to schedule any Tasks created with this TaskFactory{TResult}. A null value
        /// indicates that TaskScheduler.Current should be used.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument or the <paramref name="continuationOptions"/>
        /// argumentspecifies an invalid value.
        /// </exception>
        /// <remarks>
        /// With this constructor, the
        /// <see cref="TaskCreationOptions"/> property is initialized to <paramref name="creationOptions"/>,
        /// the
        /// <see cref="TaskContinuationOptions"/> property is initialized to <paramref
        /// name="continuationOptions"/>, and the <see
        /// cref="TaskScheduler">TaskScheduler</see> property is initialized to
        /// <paramref name="scheduler"/>, unless it's null, in which case the property is initialized to the
        /// current scheduler (see <see
        /// cref="TaskScheduler.Current">TaskScheduler.Current</see>).
        /// </remarks>
        public TaskFactory(CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions, TaskScheduler? scheduler)
            : this(creationOptions, continuationOptions)
        {
            m_defaultCancellationToken = cancellationToken;
            m_defaultScheduler = scheduler;
        }

        /* Properties */

        /// <summary>
        /// Gets the default <see cref="Threading.CancellationToken">CancellationToken</see> of this
        /// TaskFactory.
        /// </summary>
        /// <remarks>
        /// This property returns the default <see cref="CancellationToken"/> that will be assigned to all
        /// tasks created by this factory unless another CancellationToken value is explicitly specified
        /// during the call to the factory methods.
        /// </remarks>
        public CancellationToken CancellationToken => m_defaultCancellationToken;

        /// <summary>
        /// Gets the <see cref="TaskScheduler">TaskScheduler</see> of this
        /// TaskFactory{TResult}.
        /// </summary>
        /// <remarks>
        /// This property returns the default scheduler for this factory.  It will be used to schedule all
        /// tasks unless another scheduler is explicitly specified during calls to this factory's methods.
        /// If null, <see cref="TaskScheduler.Current">TaskScheduler.Current</see>
        /// will be used.
        /// </remarks>
        public TaskScheduler? Scheduler => m_defaultScheduler;

        /// <summary>
        /// Gets the <see cref="TaskCreationOptions">TaskCreationOptions
        /// </see> value of this TaskFactory{TResult}.
        /// </summary>
        /// <remarks>
        /// This property returns the default creation options for this factory.  They will be used to create all
        /// tasks unless other options are explicitly specified during calls to this factory's methods.
        /// </remarks>
        public TaskCreationOptions CreationOptions => m_defaultCreationOptions;

        /// <summary>
        /// Gets the <see cref="TaskCreationOptions">TaskContinuationOptions
        /// </see> value of this TaskFactory{TResult}.
        /// </summary>
        /// <remarks>
        /// This property returns the default continuation options for this factory.  They will be used to create
        /// all continuation tasks unless other options are explicitly specified during calls to this factory's methods.
        /// </remarks>
        public TaskContinuationOptions ContinuationOptions => m_defaultContinuationOptions;

        /* StartNew */

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<TResult> function)
        {
            Task? currTask = Task.InternalCurrent;
            return Task<TResult>.StartNew(currTask, function, m_defaultCancellationToken,
                m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(currTask));
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<TResult> function, CancellationToken cancellationToken)
        {
            Task? currTask = Task.InternalCurrent;
            return Task<TResult>.StartNew(currTask, function, cancellationToken,
                m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(currTask));
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <param name="creationOptions">A TaskCreationOptions value that controls the behavior of the
        /// created
        /// <see cref="Task{TResult}"/>.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<TResult> function, TaskCreationOptions creationOptions)
        {
            Task? currTask = Task.InternalCurrent;
            return Task<TResult>.StartNew(currTask, function, m_defaultCancellationToken,
                creationOptions, InternalTaskOptions.None, GetDefaultScheduler(currTask));
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <param name="creationOptions">A TaskCreationOptions value that controls the behavior of the
        /// created
        /// <see cref="Task{TResult}"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <param name="scheduler">The <see
        /// cref="TaskScheduler">TaskScheduler</see>
        /// that is used to schedule the created <see cref="Task{TResult}">
        /// Task{TResult}</see>.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="scheduler"/>
        /// argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<TResult> function, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return Task<TResult>.StartNew(
                Task.InternalCurrentIfAttached(creationOptions), function, cancellationToken,
                creationOptions, InternalTaskOptions.None, scheduler);
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="function"/>
        /// delegate.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<object?, TResult> function, object? state)
        {
            Task? currTask = Task.InternalCurrent;
            return Task<TResult>.StartNew(currTask, function, state, m_defaultCancellationToken,
                m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(currTask));
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="function"/>
        /// delegate.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<object?, TResult> function, object? state, CancellationToken cancellationToken)
        {
            Task? currTask = Task.InternalCurrent;
            return Task<TResult>.StartNew(currTask, function, state, cancellationToken,
                m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(currTask));
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="function"/>
        /// delegate.</param>
        /// <param name="creationOptions">A TaskCreationOptions value that controls the behavior of the
        /// created
        /// <see cref="Task{TResult}"/>.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<object?, TResult> function, object? state, TaskCreationOptions creationOptions)
        {
            Task? currTask = Task.InternalCurrent;
            return Task<TResult>.StartNew(currTask, function, state, m_defaultCancellationToken,
                creationOptions, InternalTaskOptions.None, GetDefaultScheduler(currTask));
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="function">A function delegate that returns the future result to be available through
        /// the <see cref="Task{TResult}"/>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="function"/>
        /// delegate.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that will be assigned to the new task.</param>
        /// <param name="creationOptions">A TaskCreationOptions value that controls the behavior of the
        /// created
        /// <see cref="Task{TResult}"/>.</param>
        /// <param name="scheduler">The <see
        /// cref="TaskScheduler">TaskScheduler</see>
        /// that is used to schedule the created <see cref="Task{TResult}">
        /// Task{TResult}</see>.</param>
        /// <returns>The started <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="function"/>
        /// argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref
        /// name="scheduler"/>
        /// argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// Calling StartNew is functionally equivalent to creating a <see cref="Task{TResult}"/> using one
        /// of its constructors and then calling
        /// <see cref="Task.Start()">Start</see> to schedule it for execution.
        /// However, unless creation and scheduling must be separated, StartNew is the recommended approach
        /// for both simplicity and performance.
        /// </remarks>
        public Task<TResult> StartNew(Func<object?, TResult> function, object? state, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return Task<TResult>.StartNew(Task.InternalCurrentIfAttached(creationOptions), function, state, cancellationToken,
                creationOptions, InternalTaskOptions.None, scheduler);
        }

        //
        // APM Factory methods
        //

        // Common core logic for FromAsync calls.  This minimizes the chance of "drift" between overload implementations.
        private static void FromAsyncCoreLogic(
            IAsyncResult iar,
            Func<IAsyncResult, TResult>? endFunction,
            Action<IAsyncResult>? endAction,
            Task<TResult> promise,
            bool requiresSynchronization)
        {
            Exception? ex = null;
            OperationCanceledException? oce = null;
            TResult result = default!;

            try
            {
                if (endFunction != null)
                {
                    result = endFunction(iar);
                }
                else
                {
                    endAction!(iar);
                }
            }
            catch (OperationCanceledException _oce) { oce = _oce; }
            catch (Exception e) { ex = e; }
            finally
            {
                if (oce != null)
                {
                    promise.TrySetCanceled(oce.CancellationToken, oce);
                }
                else if (ex != null)
                {
                    promise.TrySetException(ex);
                }
                else
                {
                    if (TplEventSource.Log.IsEnabled())
                        TplEventSource.Log.TraceOperationEnd(promise.Id, AsyncCausalityStatus.Completed);

                    if (Task.s_asyncDebuggingEnabled)
                        Task.RemoveFromActiveTasks(promise);

                    if (requiresSynchronization)
                    {
                        promise.TrySetResult(result);
                    }
                    else
                    {
                        promise.DangerousSetResult(result);
                    }
                }
            }
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that executes an end
        /// method function when a specified <see cref="IAsyncResult">IAsyncResult</see> completes.
        /// </summary>
        /// <param name="asyncResult">The IAsyncResult whose completion should trigger the processing of the
        /// <paramref name="endMethod"/>.</param>
        /// <param name="endMethod">The function delegate that processes the completed <paramref
        /// name="asyncResult"/>.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="asyncResult"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <returns>A <see cref="Task{TResult}">Task</see> that represents the
        /// asynchronous operation.</returns>
        public Task<TResult> FromAsync(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod)
        {
            return FromAsyncImpl(asyncResult, endMethod, null, m_defaultCreationOptions, DefaultScheduler);
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that executes an end
        /// method function when a specified <see cref="IAsyncResult">IAsyncResult</see> completes.
        /// </summary>
        /// <param name="asyncResult">The IAsyncResult whose completion should trigger the processing of the
        /// <paramref name="endMethod"/>.</param>
        /// <param name="endMethod">The function delegate that processes the completed <paramref
        /// name="asyncResult"/>.</param>
        /// <param name="creationOptions">The TaskCreationOptions value that controls the behavior of the
        /// created <see cref="Task{TResult}">Task</see>.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="asyncResult"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <returns>A <see cref="Task{TResult}">Task</see> that represents the
        /// asynchronous operation.</returns>
        public Task<TResult> FromAsync(
            IAsyncResult asyncResult,
            Func<IAsyncResult, TResult> endMethod,
            TaskCreationOptions creationOptions)
        {
            return FromAsyncImpl(asyncResult, endMethod, null, creationOptions, DefaultScheduler);
        }



        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that executes an end
        /// method function when a specified <see cref="IAsyncResult">IAsyncResult</see> completes.
        /// </summary>
        /// <param name="asyncResult">The IAsyncResult whose completion should trigger the processing of the
        /// <paramref name="endMethod"/>.</param>
        /// <param name="endMethod">The function delegate that processes the completed <paramref
        /// name="asyncResult"/>.</param>
        /// <param name="scheduler">The <see cref="TaskScheduler">TaskScheduler</see>
        /// that is used to schedule the task that executes the end method.</param>
        /// <param name="creationOptions">The TaskCreationOptions value that controls the behavior of the
        /// created <see cref="Task{TResult}">Task</see>.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="asyncResult"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="scheduler"/> argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <returns>A <see cref="Task{TResult}">Task</see> that represents the
        /// asynchronous operation.</returns>
        public Task<TResult> FromAsync(
            IAsyncResult asyncResult,
            Func<IAsyncResult, TResult> endMethod,
            TaskCreationOptions creationOptions,
            TaskScheduler scheduler)
        {
            return FromAsyncImpl(asyncResult, endMethod, null, creationOptions, scheduler);
        }

        // We need this logic broken out into a static method so that the similar TaskFactory.FromAsync()
        // method can access the logic w/o declaring a TaskFactory<TResult> instance.
        internal static Task<TResult> FromAsyncImpl(
            IAsyncResult asyncResult,
            Func<IAsyncResult, TResult>? endFunction,
            Action<IAsyncResult>? endAction,
            TaskCreationOptions creationOptions,
            TaskScheduler scheduler)
        {
            if (asyncResult == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.asyncResult);

            if (endFunction == null && endAction == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.endMethod);

            if (scheduler == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);

            TaskFactory.CheckFromAsyncOptions(creationOptions, false);

            return FromAsyncImplCore(asyncResult, endFunction, endAction, creationOptions, scheduler);
        }

        private static Task<TResult> FromAsyncImplCore(
            IAsyncResult asyncResult,
            Func<IAsyncResult, TResult>? endFunction,
            Action<IAsyncResult>? endAction,
            TaskCreationOptions creationOptions,
            TaskScheduler scheduler)
        {
            Task<TResult> promise = new Task<TResult>((object?)null, creationOptions);

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationBegin(promise.Id, "TaskFactory.FromAsync", 0);

            if (Task.s_asyncDebuggingEnabled)
                Task.AddToActiveTasks(promise);

            // Just specify this task as detached. No matter what happens, we want endMethod
            // to be called -- even if the parent is canceled.  So we don't want to flow
            // RespectParentCancellation.
            Task t = new Task(new Action<object>(delegate
            {
                FromAsyncCoreLogic(asyncResult, endFunction, endAction, promise, requiresSynchronization: true);
            }),
                (object?)null, null,
                default, TaskCreationOptions.None, InternalTaskOptions.None, null);

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationBegin(t.Id, "TaskFactory.FromAsync Callback", 0);

            if (Task.s_asyncDebuggingEnabled)
                Task.AddToActiveTasks(t);

            if (asyncResult.IsCompleted)
            {
                try
                {
                    t.InternalRunSynchronously(scheduler, waitForCompletion: false);
                }
                catch (Exception e)
                {
                    promise.TrySetException(e); // catch and handle any scheduler exceptions
                }
            }
            else
            {
                ThreadPool.RegisterWaitForSingleObject(
                   asyncResult.AsyncWaitHandle,
                   delegate
                   {
                       try
                       {
                           t.InternalRunSynchronously(scheduler, waitForCompletion: false);
                       }
                       catch (Exception e)
                       {
                           promise.TrySetException(e); // catch and handle any scheduler exceptions
                       }
                   },
                   null,
                   Timeout.Infinite,
                   true);
            }

            return promise;
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync(
            Func<AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, object? state)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, state, m_defaultCreationOptions);
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="creationOptions">The TaskCreationOptions value that controls the behavior of the
        /// created <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync(
            Func<AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, object? state, TaskCreationOptions creationOptions)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, state, creationOptions);
        }

        // We need this logic broken out into a static method so that the similar TaskFactory.FromAsync()
        // method can access the logic w/o declaring a TaskFactory<TResult> instance.
        internal static Task<TResult> FromAsyncImpl(Func<AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult>? endFunction, Action<IAsyncResult>? endAction,
            object? state, TaskCreationOptions creationOptions)
        {
            if (beginMethod == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.beginMethod);

            if (endFunction == null && endAction == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.endMethod);

            TaskFactory.CheckFromAsyncOptions(creationOptions, true);

            Task<TResult> promise = new Task<TResult>(state, creationOptions);

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationBegin(promise.Id, "TaskFactory.FromAsync: " + beginMethod.GetMethodName(), 0);

            if (Task.s_asyncDebuggingEnabled)
                Task.AddToActiveTasks(promise);

            try
            {
                // if we don't require synchronization, a faster set result path is taken
                IAsyncResult asyncResult = beginMethod(iar =>
                {
                    if (!iar.CompletedSynchronously)
                        FromAsyncCoreLogic(iar, endFunction, endAction, promise, requiresSynchronization: true);
                }, state);
                if (asyncResult.CompletedSynchronously)
                {
                    FromAsyncCoreLogic(asyncResult, endFunction, endAction, promise, requiresSynchronization: false);
                }
            }
            catch
            {
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationEnd(promise.Id, AsyncCausalityStatus.Error);

                if (Task.s_asyncDebuggingEnabled)
                    Task.RemoveFromActiveTasks(promise);

                // Make sure we don't leave promise "dangling".
                promise.TrySetResult();
                throw;
            }

            return promise;
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument passed to the <paramref
        /// name="beginMethod"/> delegate.</typeparam>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="arg1">The first argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync<TArg1>(
            Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod,
            TArg1 arg1, object? state)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, arg1, state, m_defaultCreationOptions);
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument passed to the <paramref
        /// name="beginMethod"/> delegate.</typeparam>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="arg1">The first argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="creationOptions">The TaskCreationOptions value that controls the behavior of the
        /// created <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync<TArg1>(
            Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod,
            TArg1 arg1, object? state, TaskCreationOptions creationOptions)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, arg1, state, creationOptions);
        }

        // We need this logic broken out into a static method so that the similar TaskFactory.FromAsync()
        // method can access the logic w/o declaring a TaskFactory<TResult> instance.
        internal static Task<TResult> FromAsyncImpl<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult>? endFunction, Action<IAsyncResult>? endAction,
            TArg1 arg1, object? state, TaskCreationOptions creationOptions)
        {
            if (beginMethod == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.beginMethod);

            if (endFunction == null && endAction == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.endFunction);

            TaskFactory.CheckFromAsyncOptions(creationOptions, true);

            Task<TResult> promise = new Task<TResult>(state, creationOptions);

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationBegin(promise.Id, "TaskFactory.FromAsync: " + beginMethod.GetMethodName(), 0);

            if (Task.s_asyncDebuggingEnabled)
                Task.AddToActiveTasks(promise);

            try
            {
                // if we don't require synchronization, a faster set result path is taken
                IAsyncResult asyncResult = beginMethod(arg1, iar =>
                {
                    if (!iar.CompletedSynchronously)
                        FromAsyncCoreLogic(iar, endFunction, endAction, promise, requiresSynchronization: true);
                }, state);
                if (asyncResult.CompletedSynchronously)
                {
                    FromAsyncCoreLogic(asyncResult, endFunction, endAction, promise, requiresSynchronization: false);
                }
            }
            catch
            {
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationEnd(promise.Id, AsyncCausalityStatus.Error);

                if (Task.s_asyncDebuggingEnabled)
                    Task.RemoveFromActiveTasks(promise);

                // Make sure we don't leave promise "dangling".
                promise.TrySetResult();
                throw;
            }

            return promise;
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument passed to the <paramref
        /// name="beginMethod"/> delegate.</typeparam>
        /// <typeparam name="TArg2">The type of the second argument passed to <paramref name="beginMethod"/>
        /// delegate.</typeparam>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="arg1">The first argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="arg2">The second argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync<TArg1, TArg2>(
            Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod,
            TArg1 arg1, TArg2 arg2, object? state)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, state, m_defaultCreationOptions);
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument passed to the <paramref
        /// name="beginMethod"/> delegate.</typeparam>
        /// <typeparam name="TArg2">The type of the second argument passed to <paramref name="beginMethod"/>
        /// delegate.</typeparam>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="arg1">The first argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="arg2">The second argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="creationOptions">The TaskCreationOptions value that controls the behavior of the
        /// created <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync<TArg1, TArg2>(
            Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod,
            TArg1 arg1, TArg2 arg2, object? state, TaskCreationOptions creationOptions)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, state, creationOptions);
        }

        // We need this logic broken out into a static method so that the similar TaskFactory.FromAsync()
        // method can access the logic w/o declaring a TaskFactory<TResult> instance.
        internal static Task<TResult> FromAsyncImpl<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult>? endFunction, Action<IAsyncResult>? endAction,
            TArg1 arg1, TArg2 arg2, object? state, TaskCreationOptions creationOptions)
        {
            if (beginMethod == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.beginMethod);

            if (endFunction == null && endAction == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.endMethod);

            Debug.Assert((endFunction != null) != (endAction != null), "Both endFunction and endAction were non-null");

            TaskFactory.CheckFromAsyncOptions(creationOptions, true);

            Task<TResult> promise = new Task<TResult>(state, creationOptions);

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationBegin(promise.Id, "TaskFactory.FromAsync: " + beginMethod.GetMethodName(), 0);

            if (Task.s_asyncDebuggingEnabled)
                Task.AddToActiveTasks(promise);

            try
            {
                // if we don't require synchronization, a faster set result path is taken
                IAsyncResult asyncResult = beginMethod(arg1, arg2, iar =>
                {
                    if (!iar.CompletedSynchronously)
                        FromAsyncCoreLogic(iar, endFunction, endAction, promise, requiresSynchronization: true);
                }, state);
                if (asyncResult.CompletedSynchronously)
                {
                    FromAsyncCoreLogic(asyncResult, endFunction, endAction, promise, requiresSynchronization: false);
                }
            }
            catch
            {
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationEnd(promise.Id, AsyncCausalityStatus.Error);

                if (Task.s_asyncDebuggingEnabled)
                    Task.RemoveFromActiveTasks(promise);

                // Make sure we don't leave promise "dangling".
                promise.TrySetResult();
                throw;
            }

            return promise;
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument passed to the <paramref
        /// name="beginMethod"/> delegate.</typeparam>
        /// <typeparam name="TArg2">The type of the second argument passed to <paramref name="beginMethod"/>
        /// delegate.</typeparam>
        /// <typeparam name="TArg3">The type of the third argument passed to <paramref name="beginMethod"/>
        /// delegate.</typeparam>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="arg1">The first argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="arg2">The second argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="arg3">The third argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3>(
            Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod,
            TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, arg3, state, m_defaultCreationOptions);
        }

        /// <summary>
        /// Creates a <see cref="Task{TResult}">Task</see> that represents a pair of
        /// begin and end methods that conform to the Asynchronous Programming Model pattern.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument passed to the <paramref
        /// name="beginMethod"/> delegate.</typeparam>
        /// <typeparam name="TArg2">The type of the second argument passed to <paramref name="beginMethod"/>
        /// delegate.</typeparam>
        /// <typeparam name="TArg3">The type of the third argument passed to <paramref name="beginMethod"/>
        /// delegate.</typeparam>
        /// <param name="beginMethod">The delegate that begins the asynchronous operation.</param>
        /// <param name="endMethod">The delegate that ends the asynchronous operation.</param>
        /// <param name="arg1">The first argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="arg2">The second argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="arg3">The third argument passed to the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <param name="creationOptions">The TaskCreationOptions value that controls the behavior of the
        /// created <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="state">An object containing data to be used by the <paramref name="beginMethod"/>
        /// delegate.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="beginMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="endMethod"/> argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="creationOptions"/> argument specifies an invalid TaskCreationOptions
        /// value.</exception>
        /// <returns>The created <see cref="Task{TResult}">Task</see> that
        /// represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method throws any exceptions thrown by the <paramref name="beginMethod"/>.
        /// </remarks>
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3>(
            Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod,
            TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state, TaskCreationOptions creationOptions)
        {
            return FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, arg3, state, creationOptions);
        }

        // We need this logic broken out into a static method so that the similar TaskFactory.FromAsync()
        // method can access the logic w/o declaring a TaskFactory<TResult> instance.
        internal static Task<TResult> FromAsyncImpl<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult>? endFunction, Action<IAsyncResult>? endAction,
            TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state, TaskCreationOptions creationOptions)
        {
            if (beginMethod == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.beginMethod);

            if (endFunction == null && endAction == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.endMethod);

            TaskFactory.CheckFromAsyncOptions(creationOptions, true);

            Task<TResult> promise = new Task<TResult>(state, creationOptions);

            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationBegin(promise.Id, "TaskFactory.FromAsync: " + beginMethod.GetMethodName(), 0);

            if (Task.s_asyncDebuggingEnabled)
                Task.AddToActiveTasks(promise);

            try
            {
                // if we don't require synchronization, a faster set result path is taken
                IAsyncResult asyncResult = beginMethod(arg1, arg2, arg3, iar =>
                {
                    if (!iar.CompletedSynchronously)
                        FromAsyncCoreLogic(iar, endFunction, endAction, promise, requiresSynchronization: true);
                }, state);
                if (asyncResult.CompletedSynchronously)
                {
                    FromAsyncCoreLogic(asyncResult, endFunction, endAction, promise, requiresSynchronization: false);
                }
            }
            catch
            {
                if (TplEventSource.Log.IsEnabled())
                    TplEventSource.Log.TraceOperationEnd(promise.Id, AsyncCausalityStatus.Error);

                if (Task.s_asyncDebuggingEnabled)
                    Task.RemoveFromActiveTasks(promise);

                // Make sure we don't leave the promise "dangling".
                promise.TrySetResult();
                throw;
            }

            return promise;
        }

        /// <summary>
        /// Special internal-only FromAsync support used by System.IO to wrap
        /// APM implementations with minimal overhead, avoiding unnecessary closure
        /// and delegate allocations.
        /// </summary>
        /// <typeparam name="TInstance">Specifies the type of the instance on which the APM implementation lives.</typeparam>
        /// <typeparam name="TArgs">Specifies the type containing the arguments.</typeparam>
        /// <param name="thisRef">The instance from which the begin and end methods are invoked.</param>
        /// <param name="beginMethod">The begin method.</param>
        /// <param name="endMethod">The end method.</param>
        /// <param name="args">The arguments.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal static Task<TResult> FromAsyncTrim<TInstance, TArgs>(
            TInstance thisRef, TArgs args,
            Func<TInstance, TArgs, AsyncCallback, object, IAsyncResult> beginMethod,
            Func<TInstance, IAsyncResult, TResult> endMethod)
            where TInstance : class
        {

            // Create the promise and start the operation.
            // No try/catch is necessary here as we want exceptions to bubble out, and because
            // the task doesn't have AttachedToParent set on it, there's no need to complete it in
            // case of an exception occurring... we can just let it go unresolved.
            var promise = new FromAsyncTrimPromise<TInstance>(thisRef, endMethod);
            IAsyncResult asyncResult = beginMethod(thisRef, args, FromAsyncTrimPromise<TInstance>.s_completeFromAsyncResult, promise);

            // If the IAsyncResult completed asynchronously, completing the promise will be handled by the callback.
            // If it completed synchronously, we'll handle that here.
            if (asyncResult.CompletedSynchronously)
            {
                promise.Complete(thisRef, endMethod, asyncResult, requiresSynchronization: false);
            }

            // Return the promise
            return promise;
        }

        /// <summary>
        /// A specialized task used by FromAsyncTrim.  Stores relevant information as instance
        /// state so that we can avoid unnecessary closure/delegate allocations.
        /// </summary>
        /// <typeparam name="TInstance">Specifies the type of the instance on which the APM implementation lives.</typeparam>
        private sealed class FromAsyncTrimPromise<TInstance> : Task<TResult> where TInstance : class
        {
            /// <summary>A cached delegate used as the callback for the BeginXx method.</summary>
            internal static readonly AsyncCallback s_completeFromAsyncResult = CompleteFromAsyncResult;

            /// <summary>A reference to the object on which the begin/end methods are invoked.</summary>
            private TInstance? m_thisRef;
            /// <summary>The end method.</summary>
            private Func<TInstance, IAsyncResult, TResult>? m_endMethod;

            /// <summary>Initializes the promise.</summary>
            /// <param name="thisRef">A reference to the object on which the begin/end methods are invoked.</param>
            /// <param name="endMethod">The end method.</param>
            internal FromAsyncTrimPromise(TInstance thisRef, Func<TInstance, IAsyncResult, TResult> endMethod)
            {
                m_thisRef = thisRef;
                m_endMethod = endMethod;
            }

            /// <summary>
            /// Completes the asynchronous operation using information in the IAsyncResult.
            /// IAsyncResult.AsyncState needs to be the FromAsyncTrimPromise to complete.
            /// </summary>
            /// <param name="asyncResult">The IAsyncResult for the async operation.</param>
            internal static void CompleteFromAsyncResult(IAsyncResult asyncResult)
            {
                // Validate argument
                if (asyncResult == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.asyncResult);

                var promise = asyncResult.AsyncState as FromAsyncTrimPromise<TInstance>;
                if (promise == null) ThrowHelper.ThrowArgumentException(ExceptionResource.InvalidOperation_WrongAsyncResultOrEndCalledMultiple, ExceptionArgument.asyncResult);

                // Grab the relevant state and then null it out so that the task doesn't hold onto the state unnecessarily
                TInstance? thisRef = promise.m_thisRef;
                Func<TInstance, IAsyncResult, TResult>? endMethod = promise.m_endMethod;
                promise.m_thisRef = default;
                promise.m_endMethod = null;
                if (endMethod == null) ThrowHelper.ThrowArgumentException(ExceptionResource.InvalidOperation_WrongAsyncResultOrEndCalledMultiple, ExceptionArgument.asyncResult);

                // Complete the promise.  If the IAsyncResult completed synchronously,
                // we'll instead complete the promise at the call site.
                if (!asyncResult.CompletedSynchronously)
                {
                    promise.Complete(thisRef, endMethod, asyncResult, requiresSynchronization: true);
                }
            }

            /// <summary>Completes the promise.</summary>
            /// <param name="thisRef">The target instance on which the end method should be called.</param>
            /// <param name="endMethod">The end method to call to retrieve the result.</param>
            /// <param name="asyncResult">The IAsyncResult for the async operation.</param>
            /// <param name="requiresSynchronization">
            /// Whether completing the task requires synchronization.  This should be true
            /// unless absolutely sure that the task has not yet been handed out to any consumers.
            /// </param>
            internal void Complete(
                TInstance thisRef, Func<TInstance, IAsyncResult, TResult> endMethod, IAsyncResult asyncResult,
                bool requiresSynchronization)
            {
                // Run the end method and complete the task
                bool successfullySet;
                try
                {
                    TResult result = endMethod(thisRef, asyncResult);
                    if (requiresSynchronization)
                    {
                        successfullySet = TrySetResult(result);
                    }
                    else
                    {
                        // If requiresSynchronization is false, we can use the DangerousSetResult
                        // method, which uses no synchronization to complete the task.  This is
                        // only valid when the operation is completing synchronously such
                        // that the task has not yet been handed out to any consumers.
                        DangerousSetResult(result);
                        successfullySet = true;
                    }
                }
                catch (OperationCanceledException oce)
                {
                    successfullySet = TrySetCanceled(oce.CancellationToken, oce);
                }
                catch (Exception exc)
                {
                    successfullySet = TrySetException(exc);
                }
            }
        }

        // Utility method to create a canceled future-style task.
        // Used by ContinueWhenAll/Any to bail out early on a pre-canceled token.
        private static Task<TResult> CreateCanceledTask(TaskContinuationOptions continuationOptions, CancellationToken ct)
        {
            Task.CreationOptionsFromContinuationOptions(continuationOptions, out TaskCreationOptions tco, out _);
            return new Task<TResult>(true, default, tco, ct);
        }

        //
        // ContinueWhenAll() methods
        //

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in
        /// the <paramref name="tasks"/> array have completed.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in
        /// the <paramref name="tasks"/> array have completed.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in the <paramref
        /// name="tasks"/> array have completed.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAll.
        /// </remarks>
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in the <paramref
        /// name="tasks"/> array have completed.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="scheduler">The <see cref="TaskScheduler">TaskScheduler</see>
        /// that is used to schedule the created continuation <see
        /// cref="Task">Task</see>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="scheduler"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAll.
        /// </remarks>
        public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in the
        /// <paramref name="tasks"/> array have completed.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in the
        /// <paramref name="tasks"/> array have completed.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction,
            CancellationToken cancellationToken)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in the
        /// <paramref name="tasks"/> array have completed.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAll.
        /// </remarks>
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction,
            TaskContinuationOptions continuationOptions)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of a set of provided Tasks.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue.</param>
        /// <param name="continuationFunction">The function delegate to execute when all tasks in the
        /// <paramref name="tasks"/> array have completed.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="scheduler">The <see cref="TaskScheduler">TaskScheduler</see>
        /// that is used to schedule the created continuation <see
        /// cref="Task{TResult}"/>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="scheduler"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAll.
        /// </remarks>
        public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }


        // Core implementation of ContinueWhenAll -- the generic version
        // Note: if you make any changes to this method, please do the same to the non-generic version too.
        internal static Task<TResult> ContinueWhenAllImpl<TAntecedentResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>[], TResult>? continuationFunction, Action<Task<TAntecedentResult>[]>? continuationAction,
            TaskContinuationOptions continuationOptions, CancellationToken cancellationToken, TaskScheduler scheduler)
        {
            TaskFactory.CheckMultiTaskContinuationOptions(continuationOptions);
            if (tasks == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            if (scheduler == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);

            // Check tasks array and make defensive copy
            Task<TAntecedentResult>[] tasksCopy = TaskFactory.CheckMultiContinuationTasksAndCopy(tasks);

            // Bail early if cancellation has been requested.
            if (cancellationToken.IsCancellationRequested
                && ((continuationOptions & TaskContinuationOptions.LazyCancellation) == 0)
                )
            {
                return CreateCanceledTask(continuationOptions, cancellationToken);
            }

            // Call common ContinueWhenAll() setup logic, extract starter task.
            Task<Task<TAntecedentResult>[]> starter = TaskFactory.CommonCWAllLogic(tasksCopy);

            // returned continuation task, off of starter
            if (continuationFunction != null)
            {
                return starter.ContinueWith(
                   static (starter, continuationFunction) => ((Func<Task<TAntecedentResult>[], TResult>)continuationFunction!)(starter.Result),
                   continuationFunction, scheduler, cancellationToken, continuationOptions);
            }
            else
            {
                return starter.ContinueWith(
                   static (starter, continuationAction) =>
                   {
                       ((Action<Task<TAntecedentResult>[]>)continuationAction!)(starter.Result);
                       return default(TResult)!;
                   },
                   continuationAction, scheduler, cancellationToken, continuationOptions);
            }
        }

        // Core implementation of ContinueWhenAll -- the non-generic version
        // Note: if you make any changes to this method, please do the same to the generic version too.
        internal static Task<TResult> ContinueWhenAllImpl(Task[] tasks,
            Func<Task[], TResult>? continuationFunction, Action<Task[]>? continuationAction,
            TaskContinuationOptions continuationOptions, CancellationToken cancellationToken, TaskScheduler scheduler)
        {
            TaskFactory.CheckMultiTaskContinuationOptions(continuationOptions);
            if (tasks == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            if (scheduler == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);

            // Check tasks array and make defensive copy
            Task[] tasksCopy = TaskFactory.CheckMultiContinuationTasksAndCopy(tasks);

            // Bail early if cancellation has been requested.
            if (cancellationToken.IsCancellationRequested
                && ((continuationOptions & TaskContinuationOptions.LazyCancellation) == 0)
                )
            {
                return CreateCanceledTask(continuationOptions, cancellationToken);
            }

            // Perform common ContinueWhenAll() setup logic, extract starter task
            Task<Task[]> starter = TaskFactory.CommonCWAllLogic(tasksCopy);

            // returned continuation task, off of starter
            if (continuationFunction != null)
            {
                return starter.ContinueWith(
                    static (completedTasks, state) =>
                    {
                        completedTasks.NotifyDebuggerOfWaitCompletionIfNecessary();
                        Debug.Assert(state is Func<Task[], TResult>);
                        return ((Func<Task[], TResult>)state)(completedTasks.Result);
                    },
                    continuationFunction, scheduler, cancellationToken, continuationOptions);
            }
            else
            {
                return starter.ContinueWith<TResult>(
                   static (completedTasks, state) =>
                   {
                       completedTasks.NotifyDebuggerOfWaitCompletionIfNecessary();
                       Debug.Assert(state is Action<Task[]>);
                       ((Action<Task[]>)state)(completedTasks.Result); return default!;
                   },
                   continuationAction, scheduler, cancellationToken, continuationOptions);
            }
        }

        //
        // ContinueWhenAny() methods
        //

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the <paramref
        /// name="tasks"/> array completes.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the <paramref
        /// name="tasks"/> array completes.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the <paramref
        /// name="tasks"/> array completes.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAny.
        /// </remarks>
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the <paramref
        /// name="tasks"/> array completes.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="scheduler">The <see cref="TaskScheduler">TaskScheduler</see>
        /// that is used to schedule the created continuation <see
        /// cref="Task">Task</see>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}">Task</see>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="scheduler"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAny.
        /// </remarks>
        public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the
        /// <paramref name="tasks"/> array completes.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the
        /// <paramref name="tasks"/> array completes.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction,
            CancellationToken cancellationToken)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the
        /// <paramref name="tasks"/> array completes.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAny.
        /// </remarks>
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction,
            TaskContinuationOptions continuationOptions)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        /// <summary>
        /// Creates a continuation <see cref="Task{TResult}">Task</see>
        /// that will be started upon the completion of any Task in the provided set.
        /// </summary>
        /// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
        /// <param name="tasks">The array of tasks from which to continue when one task completes.</param>
        /// <param name="continuationFunction">The function delegate to execute when one task in the
        /// <paramref name="tasks"/> array completes.</param>
        /// <param name="cancellationToken">The <see cref="Threading.CancellationToken">CancellationToken</see>
        /// that will be assigned to the new continuation task.</param>
        /// <param name="continuationOptions">The <see cref="TaskContinuationOptions">
        /// TaskContinuationOptions</see> value that controls the behavior of
        /// the created continuation <see cref="Task{TResult}">Task</see>.</param>
        /// <param name="scheduler">The <see cref="TaskScheduler">TaskScheduler</see>
        /// that is used to schedule the created continuation <see
        /// cref="Task{TResult}"/>.</param>
        /// <returns>The new continuation <see cref="Task{TResult}"/>.</returns>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="continuationFunction"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the
        /// <paramref name="scheduler"/> argument is null.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array contains a null value.</exception>
        /// <exception cref="ArgumentException">The exception that is thrown when the
        /// <paramref name="tasks"/> array is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The exception that is thrown when the
        /// <paramref name="continuationOptions"/> argument specifies an invalid TaskContinuationOptions
        /// value.</exception>
        /// <exception cref="ObjectDisposedException">The provided <see cref="Threading.CancellationToken">CancellationToken</see>
        /// has already been disposed.
        /// </exception>
        /// <remarks>
        /// The NotOn* and OnlyOn* <see cref="TaskContinuationOptions">TaskContinuationOptions</see>,
        /// which constrain for which <see cref="TaskStatus">TaskStatus</see> states a continuation
        /// will be executed, are illegal with ContinueWhenAny.
        /// </remarks>
        public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            if (continuationFunction == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuationFunction);

            return ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }

        // Core implementation of ContinueWhenAny, non-generic version
        // Note: if you make any changes to this method, be sure to do the same to the generic version
        internal static Task<TResult> ContinueWhenAnyImpl(Task[] tasks,
            Func<Task, TResult>? continuationFunction, Action<Task>? continuationAction,
            TaskContinuationOptions continuationOptions, CancellationToken cancellationToken, TaskScheduler scheduler)
        {
            TaskFactory.CheckMultiTaskContinuationOptions(continuationOptions);
            if (tasks == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            if (tasks.Length == 0) ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_EmptyTaskList, ExceptionArgument.tasks);

            if (scheduler == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);

            // Call common ContinueWhenAny() setup logic, extract starter
            Task<Task> starter = TaskFactory.CommonCWAnyLogic(tasks);

            // Bail early if cancellation has been requested.
            if (cancellationToken.IsCancellationRequested
                && ((continuationOptions & TaskContinuationOptions.LazyCancellation) == 0)
                )
            {
                return CreateCanceledTask(continuationOptions, cancellationToken);
            }

            // returned continuation task, off of starter
            if (continuationFunction != null)
            {
                return starter.ContinueWith(
                     static (completedTask, state) =>
                     {
                         Debug.Assert(state is Func<Task, TResult>);
                         return ((Func<Task, TResult>)state)(completedTask.Result);
                     },
                     continuationFunction, scheduler, cancellationToken, continuationOptions);
            }
            else
            {
                return starter.ContinueWith<TResult>(
                    static (completedTask, state) =>
                    {
                        Debug.Assert(state is Action<Task>);
                        ((Action<Task>)state)(completedTask.Result);
                        return default!;
                    },
                    continuationAction, scheduler, cancellationToken, continuationOptions);
            }
        }


        // Core implementation of ContinueWhenAny, generic version
        // Note: if you make any changes to this method, be sure to do the same to the non-generic version
        internal static Task<TResult> ContinueWhenAnyImpl<TAntecedentResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>, TResult>? continuationFunction, Action<Task<TAntecedentResult>>? continuationAction,
            TaskContinuationOptions continuationOptions, CancellationToken cancellationToken, TaskScheduler scheduler)
        {
            TaskFactory.CheckMultiTaskContinuationOptions(continuationOptions);
            if (tasks == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.tasks);
            if (tasks.Length == 0) ThrowHelper.ThrowArgumentException(ExceptionResource.Task_MultiTaskContinuation_EmptyTaskList, ExceptionArgument.tasks);
            if (scheduler == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);

            // Call common ContinueWhenAny setup logic, extract starter
            Task<Task<TAntecedentResult>> starter = TaskFactory.CommonCWAnyLogic(tasks);

            // Bail early if cancellation has been requested.
            if (cancellationToken.IsCancellationRequested
                && ((continuationOptions & TaskContinuationOptions.LazyCancellation) == 0)
                )
            {
                return CreateCanceledTask(continuationOptions, cancellationToken);
            }

            // returned continuation task, off of starter
            if (continuationFunction != null)
            {
                return starter.ContinueWith(
                    static (starter, continuationFunction) => ((Func<Task<TAntecedentResult>, TResult>)continuationFunction!)(starter.Result),
                    continuationFunction, scheduler, cancellationToken, continuationOptions);
            }
            else
            {
                return starter.ContinueWith(
                    static (starter, continuationAction) =>
                    {
                        ((Action<Task<TAntecedentResult>>)continuationAction!)(starter.Result);
                        return default(TResult)!;
                    },
                    continuationAction, scheduler, cancellationToken, continuationOptions);
            }
        }
    }

    /// <summary>Provides an event source for tracing TPL information.</summary>

    [EventSource(Name = "System.Threading.Tasks.TplEventSource", Guid = "2e5dba47-a3d2-4d16-8ee0-6671ffdcd7b5")]
    internal sealed class TplEventSource : EventSource
    {
        public enum TaskWaitBehavior
        {
            Synchronous = 1,
            Asynchronous
        }

        public static class Tasks
        {
            public const EventTask Loop = (EventTask)1;

            public const EventTask Invoke = (EventTask)2;

            public const EventTask TaskExecute = (EventTask)3;

            public const EventTask TaskWait = (EventTask)4;

            public const EventTask ForkJoin = (EventTask)5;

            public const EventTask TaskScheduled = (EventTask)6;

            public const EventTask AwaitTaskContinuationScheduled = (EventTask)7;

            public const EventTask TraceOperation = (EventTask)8;

            public const EventTask TraceSynchronousWork = (EventTask)9;
        }

        public static class Keywords
        {
            public const EventKeywords TaskTransfer = (EventKeywords)1L;

            public const EventKeywords Tasks = (EventKeywords)2L;

            public const EventKeywords Parallel = (EventKeywords)4L;

            public const EventKeywords AsyncCausalityOperation = (EventKeywords)8L;

            public const EventKeywords AsyncCausalityRelation = (EventKeywords)16L;

            public const EventKeywords AsyncCausalitySynchronousWork = (EventKeywords)32L;

            public const EventKeywords TaskStops = (EventKeywords)64L;

            public const EventKeywords TasksFlowActivityIds = (EventKeywords)128L;

            public const EventKeywords AsyncMethod = (EventKeywords)256L;

            public const EventKeywords TasksSetActivityIds = (EventKeywords)65536L;

            public const EventKeywords Debug = (EventKeywords)131072L;

            public const EventKeywords DebugActivityId = (EventKeywords)262144L;

            public const EventKeywords AsyncInstrumentation = (EventKeywords)524288L;
        }

        private const string EventSourceSuppressMessage = "Parameters to this method are primitive and are trimmer safe";

        internal bool TasksSetActivityIds;

        internal bool Debug;

        private bool DebugActivityId;

        private const int DefaultAppDomainID = 1;

        public static readonly TplEventSource Log = new TplEventSource();

        private const int TASKSCHEDULED_ID = 7;

        private const int TASKSTARTED_ID = 8;

        private const int TASKCOMPLETED_ID = 9;

        private const int TASKWAITBEGIN_ID = 10;

        private const int TASKWAITEND_ID = 11;

        private const int AWAITTASKCONTINUATIONSCHEDULED_ID = 12;

        private const int TASKWAITCONTINUATIONCOMPLETE_ID = 13;

        private const int TASKWAITCONTINUATIONSTARTED_ID = 19;

        private const int TRACEOPERATIONSTART_ID = 14;

        private const int TRACEOPERATIONSTOP_ID = 15;

        private const int TRACEOPERATIONRELATION_ID = 16;

        private const int TRACESYNCHRONOUSWORKSTART_ID = 17;

        private const int TRACESYNCHRONOUSWORKSTOP_ID = 18;

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            TasksSetActivityIds = IsEnabled(EventLevel.Informational, (EventKeywords)65536L);
            Debug = IsEnabled(EventLevel.Informational, (EventKeywords)131072L);
            DebugActivityId = IsEnabled(EventLevel.Informational, (EventKeywords)262144L);
        }

        [Event(7, Task = (EventTask)6, Version = 1, Opcode = EventOpcode.Send, Level = EventLevel.Informational, Keywords = (EventKeywords)3L, Message = "Task {2} scheduled to TaskScheduler {0}.")]
        public unsafe void TaskScheduled(int OriginatingTaskSchedulerID, int OriginatingTaskID, int TaskID, int CreatingTaskID, int TaskCreationOptions, int appDomain = 1)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)3L))
            {
                EventData* ptr = stackalloc EventData[6];
                ptr->Size = 4;
                ptr->DataPointer = (nint)(&OriginatingTaskSchedulerID);
                ptr->Reserved = 0;
                ptr[1].Size = 4;
                ptr[1].DataPointer = (nint)(&OriginatingTaskID);
                ptr[1].Reserved = 0;
                ptr[2].Size = 4;
                ptr[2].DataPointer = (nint)(&TaskID);
                ptr[2].Reserved = 0;
                ptr[3].Size = 4;
                ptr[3].DataPointer = (nint)(&CreatingTaskID);
                ptr[3].Reserved = 0;
                ptr[4].Size = 4;
                ptr[4].DataPointer = (nint)(&TaskCreationOptions);
                ptr[4].Reserved = 0;
                ptr[5].Size = 4;
                ptr[5].DataPointer = (nint)(&appDomain);
                ptr[5].Reserved = 0;
                if (TasksSetActivityIds)
                {
                    Guid guid = CreateGuidForTaskID(TaskID);
                    WriteEventWithRelatedActivityIdCore(7, &guid, 6, ptr);
                }
                else
                {
                    WriteEventCore(7, 6, ptr);
                }
            }
        }

        [Event(8, Level = EventLevel.Informational, Keywords = (EventKeywords)2L, Message = "Task {2} executing.")]
        public void TaskStarted(int OriginatingTaskSchedulerID, int OriginatingTaskID, int TaskID)
        {
            if (IsEnabled(EventLevel.Informational, (EventKeywords)2L))
            {
                WriteEvent(8, OriginatingTaskSchedulerID, OriginatingTaskID, TaskID);
            }
        }

        [Event(9, Version = 1, Level = EventLevel.Informational, Keywords = (EventKeywords)64L, Message = "Task {2} completed.")]
        public unsafe void TaskCompleted(int OriginatingTaskSchedulerID, int OriginatingTaskID, int TaskID, bool IsExceptional)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)2L))
            {
                EventData* ptr = stackalloc EventData[4];
                int num = (IsExceptional ? 1 : 0);
                ptr->Size = 4;
                ptr->DataPointer = (nint)(&OriginatingTaskSchedulerID);
                ptr->Reserved = 0;
                ptr[1].Size = 4;
                ptr[1].DataPointer = (nint)(&OriginatingTaskID);
                ptr[1].Reserved = 0;
                ptr[2].Size = 4;
                ptr[2].DataPointer = (nint)(&TaskID);
                ptr[2].Reserved = 0;
                ptr[3].Size = 4;
                ptr[3].DataPointer = (nint)(&num);
                ptr[3].Reserved = 0;
                WriteEventCore(9, 4, ptr);
            }
        }

        [Event(10, Version = 3, Task = (EventTask)4, Opcode = EventOpcode.Send, Level = EventLevel.Informational, Keywords = (EventKeywords)3L, Message = "Beginning wait ({3}) on Task {2}.")]
        public unsafe void TaskWaitBegin(int OriginatingTaskSchedulerID, int OriginatingTaskID, int TaskID, TaskWaitBehavior Behavior, int ContinueWithTaskID)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)3L))
            {
                EventData* ptr = stackalloc EventData[5];
                ptr->Size = 4;
                ptr->DataPointer = (nint)(&OriginatingTaskSchedulerID);
                ptr->Reserved = 0;
                ptr[1].Size = 4;
                ptr[1].DataPointer = (nint)(&OriginatingTaskID);
                ptr[1].Reserved = 0;
                ptr[2].Size = 4;
                ptr[2].DataPointer = (nint)(&TaskID);
                ptr[2].Reserved = 0;
                ptr[3].Size = 4;
                ptr[3].DataPointer = (nint)(&Behavior);
                ptr[3].Reserved = 0;
                ptr[4].Size = 4;
                ptr[4].DataPointer = (nint)(&ContinueWithTaskID);
                ptr[4].Reserved = 0;
                if (TasksSetActivityIds)
                {
                    Guid guid = CreateGuidForTaskID(TaskID);
                    WriteEventWithRelatedActivityIdCore(10, &guid, 5, ptr);
                }
                else
                {
                    WriteEventCore(10, 5, ptr);
                }
            }
        }

        [Event(11, Level = EventLevel.Verbose, Keywords = (EventKeywords)2L, Message = "Ending wait on Task {2}.")]
        public void TaskWaitEnd(int OriginatingTaskSchedulerID, int OriginatingTaskID, int TaskID)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Verbose, (EventKeywords)2L))
            {
                WriteEvent(11, OriginatingTaskSchedulerID, OriginatingTaskID, TaskID);
            }
        }

        [Event(13, Level = EventLevel.Verbose, Keywords = (EventKeywords)64L)]
        public void TaskWaitContinuationComplete(int TaskID)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Verbose, (EventKeywords)64L))
            {
                WriteEvent(13, TaskID);
            }
        }

        [Event(19, Level = EventLevel.Verbose, Keywords = (EventKeywords)2L)]
        public void TaskWaitContinuationStarted(int TaskID)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Verbose, (EventKeywords)2L))
            {
                WriteEvent(19, TaskID);
            }
        }

        [Event(12, Task = (EventTask)7, Opcode = EventOpcode.Send, Level = EventLevel.Informational, Keywords = (EventKeywords)3L)]
        public unsafe void AwaitTaskContinuationScheduled(int OriginatingTaskSchedulerID, int OriginatingTaskID, int ContinueWithTaskId)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)3L))
            {
                EventData* ptr = stackalloc EventData[3];
                ptr->Size = 4;
                ptr->DataPointer = (nint)(&OriginatingTaskSchedulerID);
                ptr->Reserved = 0;
                ptr[1].Size = 4;
                ptr[1].DataPointer = (nint)(&OriginatingTaskID);
                ptr[1].Reserved = 0;
                ptr[2].Size = 4;
                ptr[2].DataPointer = (nint)(&ContinueWithTaskId);
                ptr[2].Reserved = 0;
                if (TasksSetActivityIds)
                {
                    Guid guid = CreateGuidForTaskID(ContinueWithTaskId);
                    WriteEventWithRelatedActivityIdCore(12, &guid, 3, ptr);
                }
                else
                {
                    WriteEventCore(12, 3, ptr);
                }
            }
        }

        [Event(14, Version = 1, Level = EventLevel.Informational, Keywords = (EventKeywords)8L)]
        public unsafe void TraceOperationBegin(int TaskID, string OperationName, long RelatedContext)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)8L))
            {
                fixed (char* dataPointer = OperationName)
                {
                    EventData* ptr = stackalloc EventData[3];
                    ptr->Size = 4;
                    ptr->DataPointer = (nint)(&TaskID);
                    ptr->Reserved = 0;
                    ptr[1].Size = (OperationName.Length + 1) * 2;
                    ptr[1].DataPointer = (nint)dataPointer;
                    ptr[1].Reserved = 0;
                    ptr[2].Size = 8;
                    ptr[2].DataPointer = (nint)(&RelatedContext);
                    ptr[2].Reserved = 0;
                    WriteEventCore(14, 3, ptr);
                }
            }
        }

        [Event(16, Version = 1, Level = EventLevel.Informational, Keywords = (EventKeywords)16L)]
        public void TraceOperationRelation(int TaskID, CausalityRelation Relation)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)16L))
            {
                WriteEvent(16, TaskID, (int)Relation);
            }
        }

        [Event(15, Version = 1, Level = EventLevel.Informational, Keywords = (EventKeywords)8L)]
        public void TraceOperationEnd(int TaskID, AsyncCausalityStatus Status)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)8L))
            {
                WriteEvent(15, TaskID, (int)Status);
            }
        }

        [Event(17, Version = 1, Level = EventLevel.Informational, Keywords = (EventKeywords)32L)]
        public void TraceSynchronousWorkBegin(int TaskID, CausalitySynchronousWork Work)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)32L))
            {
                WriteEvent(17, TaskID, (int)Work);
            }
        }

        [Event(18, Version = 1, Level = EventLevel.Informational, Keywords = (EventKeywords)32L)]
        public unsafe void TraceSynchronousWorkEnd(CausalitySynchronousWork Work)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Informational, (EventKeywords)32L))
            {
                EventData* ptr = stackalloc EventData[1];
                ptr->Size = 4;
                ptr->DataPointer = (nint)(&Work);
                ptr->Reserved = 0;
                WriteEventCore(18, 1, ptr);
            }
        }

        [NonEvent]
        public void RunningContinuation(int TaskID, object Object)
        {
            RunningContinuation(TaskID, EventSource.ObjectIDForEvents(Object));
        }

        [Event(20, Keywords = (EventKeywords)131072L)]
        private void RunningContinuation(int TaskID, long Object)
        {
            if (Debug)
            {
                WriteEvent(20, TaskID, Object);
            }
        }

        [NonEvent]
        public void RunningContinuationList(int TaskID, int Index, object Object)
        {
            RunningContinuationList(TaskID, Index, EventSource.ObjectIDForEvents(Object));
        }

        [Event(21, Keywords = (EventKeywords)131072L)]
        public void RunningContinuationList(int TaskID, int Index, long Object)
        {
            if (Debug)
            {
                WriteEvent(21, TaskID, Index, Object);
            }
        }

        [Event(23, Keywords = (EventKeywords)131072L)]
        public void DebugFacilityMessage(string Facility, string Message)
        {
            WriteEvent(23, Facility, Message);
        }

        [Event(24, Keywords = (EventKeywords)131072L)]
        public void DebugFacilityMessage1(string Facility, string Message, string Value1)
        {
            WriteEvent(24, Facility, Message, Value1);
        }

        [Event(25, Keywords = (EventKeywords)262144L)]
        public void SetActivityId(Guid NewId)
        {
            if (DebugActivityId)
            {
                WriteEvent(25, NewId);
            }
        }

        [Event(26, Keywords = (EventKeywords)131072L)]
        public void NewID(int TaskID)
        {
            if (Debug)
            {
                WriteEvent(26, TaskID);
            }
        }

        [NonEvent]
        public void IncompleteAsyncMethod(IAsyncStateMachineBox stateMachineBox)
        {
            if (IsEnabled() && IsEnabled(EventLevel.Warning, (EventKeywords)256L))
            {
                IAsyncStateMachine stateMachineObject = stateMachineBox.GetStateMachineObject();
                if (stateMachineObject is not null)
                {
                    string asyncStateMachineDescription = AsyncMethodBuilderCore.GetAsyncStateMachineDescription(stateMachineObject);
                    IncompleteAsyncMethod(asyncStateMachineDescription);
                }
            }
        }

        [Event(27, Level = EventLevel.Warning, Keywords = (EventKeywords)256L)]
        private void IncompleteAsyncMethod(string stateMachineDescription)
        {
            WriteEvent(27, stateMachineDescription);
        }

        internal static Guid CreateGuidForTaskID(int taskID)
        {
            int processId = Environment.ProcessId;
            return new Guid(taskID, 1, 0, (byte)processId, (byte)(processId >> 8), (byte)(processId >> 16), (byte)(processId >> 24), byte.MaxValue, 220, 215, 181);
        }

        private TplEventSource()
            : base("System.Threading.Tasks.TplEventSource", new Guid(777894471u, 41938, 19734, 142, 224, 102, 113, byte.MaxValue, 220, 215, 181))
        {
        }
    }

    internal enum AsyncCausalityStatus
    {
        Started,
        Completed,
        Canceled,
        Error
    }


    internal class AwaitTaskContinuation : TaskContinuation, IThreadPoolWorkItem
    {
        protected readonly ExecutionContext m_capturedContext;

        protected readonly Action m_action;

        protected int m_continuationId;

        private static readonly ContextCallback s_invokeContextCallback = delegate(object state)
        {
            ((Action)state)();
        };

        private static readonly Action<Action> s_invokeAction = delegate(Action action)
        {
            action();
        };

        internal static bool IsValidLocationForInlining
        {
            get
            {
                SynchronizationContext current = SynchronizationContext.Current;
                if (current is not null && current.GetType() != typeof(SynchronizationContext))
                {
                    return false;
                }
                TaskScheduler internalCurrent = TaskScheduler.InternalCurrent;
                if (internalCurrent is not null)
                {
                    return internalCurrent == TaskScheduler.Default;
                }
                return true;
            }
        }

        internal AwaitTaskContinuation(Action action, bool flowExecutionContext)
        {
            m_action = action;
            if (flowExecutionContext)
            {
                m_capturedContext = ExecutionContext.Capture();
            }
        }

        protected static Task CreateTask(Action<object> action, object state, TaskScheduler scheduler, ExecutionContext capturedContext)
        {
            return new Task(action, state, null, default(CancellationToken), TaskCreationOptions.None, InternalTaskOptions.QueuedByRuntime, scheduler)
            {
                CapturedContext = capturedContext
            };
        }

        internal override void Run(Task task, bool canInlineContinuationTask)
        {
            if (canInlineContinuationTask && IsValidLocationForInlining)
            {
                RunCallback(GetInvokeActionCallback(), m_action, ref Task.t_currentTask);
                return;
            }
            TplEventSource log = TplEventSource.Log;
            if (log.IsEnabled())
            {
                m_continuationId = Task.NewId();
                log.AwaitTaskContinuationScheduled((task.ExecutingTaskScheduler ?? TaskScheduler.Default).Id, task.Id, m_continuationId);
            }
            ThreadPool.UnsafeQueueUserWorkItemInternal(this, preferLocal: true);
        }

        void IThreadPoolWorkItem.Execute()
        {
            TplEventSource log = TplEventSource.Log;
            ExecutionContext capturedContext = m_capturedContext;
            if (!log.IsEnabled() && capturedContext is null)
            {
                m_action();
                return;
            }
            Guid oldActivityThatWillContinue = default(Guid);
            if (log.IsEnabled() && log.TasksSetActivityIds && m_continuationId != 0)
            {
                EventSource.SetCurrentThreadActivityId(TplEventSource.CreateGuidForTaskID(m_continuationId), out oldActivityThatWillContinue);
            }
            try
            {
                if (capturedContext is null || capturedContext.IsDefault)
                {
                    m_action();
                }
                else
                {
                    ExecutionContext.RunForThreadPoolUnsafe(capturedContext, s_invokeAction, in m_action);
                }
            }
            finally
            {
                if (log.IsEnabled() && log.TasksSetActivityIds && m_continuationId != 0)
                {
                    EventSource.SetCurrentThreadActivityId(oldActivityThatWillContinue);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static ContextCallback GetInvokeActionCallback()
        {
            return s_invokeContextCallback;
        }

        protected void RunCallback(ContextCallback callback, object state, ref Task currentTask)
        {
            Task task = currentTask;
            try
            {
                if (task is not null)
                {
                    currentTask = null;
                }
                ExecutionContext capturedContext = m_capturedContext;
                if (capturedContext is null)
                {
                    callback(state);
                }
                else
                {
                    ExecutionContext.RunInternal(capturedContext, callback, state);
                }
            }
            catch (Exception exception)
            {
                Task.ThrowAsync(exception, null);
            }
            finally
            {
                if (task is not null)
                {
                    currentTask = task;
                }
            }
        }

        internal static void RunOrScheduleAction(Action action, bool allowInlining)
        {
            Task t_currentTask = Task.t_currentTask;
            if (!allowInlining || !IsValidLocationForInlining)
            {
                UnsafeScheduleAction(action, t_currentTask);
                return;
            }
            try
            {
                if (t_currentTask is not null)
                {
                    Task.t_currentTask = null;
                }
                action();
            }
            catch (Exception exception)
            {
                Task.ThrowAsync(exception, null);
            }
            finally
            {
                if (t_currentTask is not null)
                {
                    Task.t_currentTask = t_currentTask;
                }
            }
        }

        internal static void RunOrScheduleAction(IAsyncStateMachineBox box, bool allowInlining)
        {
            Task t_currentTask = Task.t_currentTask;
            if (!allowInlining || !IsValidLocationForInlining)
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags))
                {
                    if (AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                    {
                        box = AsyncStateMachineDispatcherInfo.CreateDispatcher(box, flags);
                    }
                    if (AsyncInstrumentation.IsEnabled.Tpl(flags))
                    {
                        UnsafeScheduleAction(box.MoveNextAction, t_currentTask);
                        return;
                    }
                }
                ThreadPool.UnsafeQueueUserWorkItemInternal(box, preferLocal: true);
                return;
            }
            try
            {
                if (t_currentTask is not null)
                {
                    Task.t_currentTask = null;
                }
                box.MoveNext();
            }
            catch (Exception exception)
            {
                Task.ThrowAsync(exception, null);
            }
            finally
            {
                if (t_currentTask is not null)
                {
                    Task.t_currentTask = t_currentTask;
                }
            }
        }

        internal static void UnsafeScheduleAction(Action action, Task task)
        {
            AwaitTaskContinuation awaitTaskContinuation = new AwaitTaskContinuation(action, flowExecutionContext: false);
            TplEventSource log = TplEventSource.Log;
            if (log.IsEnabled() && task is not null)
            {
                awaitTaskContinuation.m_continuationId = Task.NewId();
                log.AwaitTaskContinuationScheduled((task.ExecutingTaskScheduler ?? TaskScheduler.Default).Id, task.Id, awaitTaskContinuation.m_continuationId);
            }
            ThreadPool.UnsafeQueueUserWorkItemInternal(awaitTaskContinuation, preferLocal: true);
        }

        internal override Delegate[] GetDelegateContinuationsForDebugger()
        {
            return new Delegate[1] { AsyncMethodBuilderCore.TryGetStateMachineForDebugger(m_action) };
        }
    }


    internal struct CachedCompletedInt32Task
    {
        private Task<int> _task;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<int> GetTask(int result)
        {
            Task<int> task = _task;
            if (task is not null && task.Result == result)
            {
                return task;
            }
            return _task = Task.FromResult(result);
        }
    }


    internal enum CausalityRelation
    {
        AssignDelegate,
        Join,
        Choice,
        Cancel,
        Error
    }


    internal enum CausalitySynchronousWork
    {
        CompletionNotification,
        ProgressNotification,
        Execution
    }


    [Flags]
    public enum ConfigureAwaitOptions
    {
        None = 0,
        ContinueOnCapturedContext = 1,
        SuppressThrowing = 2,
        ForceYielding = 4
    }


    internal sealed class ContinuationResultTaskFromResultTask<TAntecedentResult, TResult> : Task<TResult>
    {
        private Task<TAntecedentResult> m_antecedent;

        public ContinuationResultTaskFromResultTask(Task<TAntecedentResult> antecedent, Delegate function, object state, TaskCreationOptions creationOptions, InternalTaskOptions internalOptions)
            : base(function, state, Task.InternalCurrentIfAttached(creationOptions), default(CancellationToken), creationOptions, internalOptions, (TaskScheduler)null)
        {
            m_antecedent = antecedent;
        }

        internal override void InnerInvoke()
        {
            Task<TAntecedentResult> antecedent = m_antecedent;
            m_antecedent = null;
            antecedent.NotifyDebuggerOfWaitCompletionIfNecessary();
            if (m_action is Func<Task<TAntecedentResult>, TResult> func)
            {
                m_result = func(antecedent);
            }
            else if (m_action is Func<Task<TAntecedentResult>, object, TResult> func2)
            {
                m_result = func2(antecedent, m_stateObject);
            }
        }
    }


    internal sealed class ContinuationResultTaskFromTask<TResult> : Task<TResult>
    {
        private Task m_antecedent;

        public ContinuationResultTaskFromTask(Task antecedent, Delegate function, object state, TaskCreationOptions creationOptions, InternalTaskOptions internalOptions)
            : base(function, state, Task.InternalCurrentIfAttached(creationOptions), default(CancellationToken), creationOptions, internalOptions, (TaskScheduler)null)
        {
            m_antecedent = antecedent;
        }

        internal override void InnerInvoke()
        {
            Task antecedent = m_antecedent;
            m_antecedent = null;
            antecedent.NotifyDebuggerOfWaitCompletionIfNecessary();
            if (m_action is Func<Task, TResult> func)
            {
                m_result = func(antecedent);
            }
            else if (m_action is Func<Task, object, TResult> func2)
            {
                m_result = func2(antecedent, m_stateObject);
            }
        }
    }


    internal sealed class ContinuationTaskFromResultTask<TAntecedentResult> : Task
    {
        private Task<TAntecedentResult> m_antecedent;

        public ContinuationTaskFromResultTask(Task<TAntecedentResult> antecedent, Delegate action, object state, TaskCreationOptions creationOptions, InternalTaskOptions internalOptions)
            : base(action, state, Task.InternalCurrentIfAttached(creationOptions), default(CancellationToken), creationOptions, internalOptions, null)
        {
            m_antecedent = antecedent;
        }

        internal override void InnerInvoke()
        {
            Task<TAntecedentResult> antecedent = m_antecedent;
            m_antecedent = null;
            antecedent.NotifyDebuggerOfWaitCompletionIfNecessary();
            if (m_action is Action<Task<TAntecedentResult>> action)
            {
                action(antecedent);
            }
            else if (m_action is Action<Task<TAntecedentResult>, object> action2)
            {
                action2(antecedent, m_stateObject);
            }
        }
    }


    internal sealed class ContinuationTaskFromTask : Task
    {
        private Task m_antecedent;

        public ContinuationTaskFromTask(Task antecedent, Delegate action, object state, TaskCreationOptions creationOptions, InternalTaskOptions internalOptions)
            : base(action, state, Task.InternalCurrentIfAttached(creationOptions), default(CancellationToken), creationOptions, internalOptions, null)
        {
            m_antecedent = antecedent;
        }

        internal override void InnerInvoke()
        {
            Task antecedent = m_antecedent;
            m_antecedent = null;
            antecedent.NotifyDebuggerOfWaitCompletionIfNecessary();
            if (m_action is Action<Task> action)
            {
                action(antecedent);
            }
            else if (m_action is Action<Task, object> action2)
            {
                action2(antecedent, m_stateObject);
            }
        }
    }


    internal sealed class ContinueWithTaskContinuation : TaskContinuation
    {
        internal Task m_task;

        internal readonly TaskContinuationOptions m_options;

        private readonly TaskScheduler m_taskScheduler;

        internal ContinueWithTaskContinuation(Task task, TaskContinuationOptions options, TaskScheduler scheduler)
        {
            m_task = task;
            m_options = options;
            m_taskScheduler = scheduler;
            if (TplEventSource.Log.IsEnabled())
            {
                TplEventSource.Log.TraceOperationBegin(m_task.Id, "Task.ContinueWith: " + task.m_action.GetMethodName(), 0L);
            }
            if (Task.s_asyncDebuggingEnabled)
            {
                Task.AddToActiveTasks(m_task);
            }
        }

        internal override void Run(Task completedTask, bool canInlineContinuationTask)
        {
            Task task = m_task;
            m_task = null;
            TaskContinuationOptions options = m_options;
            if (completedTask.IsCompletedSuccessfully ? ((options & TaskContinuationOptions.NotOnRanToCompletion) == 0) : (completedTask.IsCanceled ? ((options & TaskContinuationOptions.NotOnCanceled) == 0) : ((options & TaskContinuationOptions.NotOnFaulted) == 0)))
            {
                if (TplEventSource.Log.IsEnabled() && !task.IsCanceled)
                {
                    TplEventSource.Log.TraceOperationRelation(task.Id, CausalityRelation.AssignDelegate);
                }
                task.m_taskScheduler = m_taskScheduler;
                if (!canInlineContinuationTask || (options & TaskContinuationOptions.ExecuteSynchronously) == 0)
                {
                    try
                    {
                        task.ScheduleAndStart(needsProtection: true);
                        return;
                    }
                    catch (TaskSchedulerException)
                    {
                        return;
                    }
                }
                TaskContinuation.InlineIfPossibleOrElseQueue(task, needsProtection: true);
            }
            else
            {
                Task.ContingentProperties contingentProperties = task.m_contingentProperties;
                if (contingentProperties is null || contingentProperties.m_cancellationToken == default(CancellationToken))
                {
                    task.InternalCancelContinueWithInitialState();
                }
                else
                {
                    task.InternalCancel();
                }
            }
        }

        internal override Delegate[] GetDelegateContinuationsForDebugger()
        {
            if (m_task is not null)
            {
                if ((object)m_task.m_action is not null)
                {
                    return new Delegate[1] { m_task.m_action };
                }
                return m_task.GetDelegateContinuationsForDebugger();
            }
            return null;
        }
    }


    internal sealed class SynchronizationContextAwaitTaskContinuation : AwaitTaskContinuation
    {
        private static readonly SendOrPostCallback s_postCallback = delegate(object state)
        {
            ((Action)state)();
        };

        private static ContextCallback s_postActionCallback;

        private readonly SynchronizationContext m_syncContext;

        internal SynchronizationContextAwaitTaskContinuation(SynchronizationContext context, Action action, bool flowExecutionContext)
            : base(action, flowExecutionContext)
        {
            m_syncContext = context;
        }

        internal sealed override void Run(Task task, bool canInlineContinuationTask)
        {
            if (canInlineContinuationTask && m_syncContext == SynchronizationContext.Current)
            {
                RunCallback(AwaitTaskContinuation.GetInvokeActionCallback(), m_action, ref Task.t_currentTask);
                return;
            }
            TplEventSource log = TplEventSource.Log;
            if (log.IsEnabled())
            {
                m_continuationId = Task.NewId();
                log.AwaitTaskContinuationScheduled((task.ExecutingTaskScheduler ?? TaskScheduler.Default).Id, task.Id, m_continuationId);
            }
            RunCallback(GetPostActionCallback(), this, ref Task.t_currentTask);
        }

        private static void PostAction(object state)
        {
            SynchronizationContextAwaitTaskContinuation synchronizationContextAwaitTaskContinuation = (SynchronizationContextAwaitTaskContinuation)state;
            TplEventSource log = TplEventSource.Log;
            if (log.IsEnabled() && log.TasksSetActivityIds && synchronizationContextAwaitTaskContinuation.m_continuationId != 0)
            {
                synchronizationContextAwaitTaskContinuation.m_syncContext.Post(s_postCallback, GetActionLogDelegate(synchronizationContextAwaitTaskContinuation.m_continuationId, synchronizationContextAwaitTaskContinuation.m_action));
            }
            else
            {
                synchronizationContextAwaitTaskContinuation.m_syncContext.Post(s_postCallback, synchronizationContextAwaitTaskContinuation.m_action);
            }
        }

        private static Action GetActionLogDelegate(int continuationId, Action action)
        {
            return delegate
            {
                EventSource.SetCurrentThreadActivityId(TplEventSource.CreateGuidForTaskID(continuationId), out var oldActivityThatWillContinue);
                try
                {
                    action();
                }
                finally
                {
                    EventSource.SetCurrentThreadActivityId(oldActivityThatWillContinue);
                }
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ContextCallback GetPostActionCallback()
        {
            return PostAction;
        }
    }


    internal sealed class SynchronizationContextTaskScheduler : TaskScheduler
    {
        private readonly SynchronizationContext m_synchronizationContext;

        private static readonly SendOrPostCallback s_postCallback = delegate(object s)
        {
            ((Task)s).ExecuteEntry();
        };

        public override int MaximumConcurrencyLevel => 1;

        internal SynchronizationContextTaskScheduler()
        {
            m_synchronizationContext = SynchronizationContext.Current ?? throw new InvalidOperationException(SR.TaskScheduler_FromCurrentSynchronizationContext_NoCurrent);
        }

        protected internal override void QueueTask(Task task)
        {
            m_synchronizationContext.Post(s_postCallback, task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (SynchronizationContext.Current == m_synchronizationContext)
            {
                return TryExecuteTask(task);
            }
            return false;
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return null;
        }
    }


    public static class TaskAsyncEnumerableExtensions
    {
        private sealed class ManualResetEventWithAwaiterSupport : ManualResetEventSlim
        {
            private readonly Action _onCompleted;

            public ManualResetEventWithAwaiterSupport()
            {
                _onCompleted = base.Set;
            }

            public void Wait<TAwaiter>(TAwaiter awaiter) where TAwaiter : ICriticalNotifyCompletion
            {
                awaiter.UnsafeOnCompleted(_onCompleted);
                Wait();
                Reset();
            }
        }

        public static ConfiguredAsyncDisposable ConfigureAwait(this IAsyncDisposable source, bool continueOnCapturedContext)
        {
            return new ConfiguredAsyncDisposable(source, continueOnCapturedContext);
        }

        public static ConfiguredCancelableAsyncEnumerable<T> ConfigureAwait<T>(this IAsyncEnumerable<T> source, bool continueOnCapturedContext) where T : allows ref struct
        {
            return new ConfiguredCancelableAsyncEnumerable<T>(source, continueOnCapturedContext, default(CancellationToken));
        }

        public static ConfiguredCancelableAsyncEnumerable<T> WithCancellation<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken) where T : allows ref struct
        {
            return new ConfiguredCancelableAsyncEnumerable<T>(source, continueOnCapturedContext: true, cancellationToken);
        }

        public static IEnumerable<T> ToBlockingEnumerable<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default(CancellationToken))
        {
            IAsyncEnumerator<T> enumerator = source.GetAsyncEnumerator(cancellationToken);
            ManualResetEventWithAwaiterSupport mres = null;
            try
            {
                while (true)
                {
                    ValueTask<bool> valueTask = enumerator.MoveNextAsync();
                    if (!valueTask.IsCompleted)
                    {
                        ManualResetEventWithAwaiterSupport manualResetEventWithAwaiterSupport = mres;
                        if (manualResetEventWithAwaiterSupport is null)
                        {
                            ManualResetEventWithAwaiterSupport manualResetEventWithAwaiterSupport2;
                            mres = (manualResetEventWithAwaiterSupport2 = new ManualResetEventWithAwaiterSupport());
                            manualResetEventWithAwaiterSupport = manualResetEventWithAwaiterSupport2;
                        }
                        manualResetEventWithAwaiterSupport.Wait(valueTask.ConfigureAwait(continueOnCapturedContext: false).GetAwaiter());
                    }
                    if (valueTask.Result)
                    {
                        yield return enumerator.Current;
                        continue;
                    }
                    break;
                }
            }
            finally
            {
                ValueTask valueTask2 = enumerator.DisposeAsync();
                if (!valueTask2.IsCompleted)
                {
                    (mres ?? new ManualResetEventWithAwaiterSupport()).Wait(valueTask2.ConfigureAwait(continueOnCapturedContext: false).GetAwaiter());
                }
                valueTask2.GetAwaiter().GetResult();
            }
        }
    }


    internal static class TaskCache
    {
        internal static readonly Task<bool> s_trueTask = CreateCacheableTask(result: true);

        internal static readonly Task<bool> s_falseTask = CreateCacheableTask(result: false);

        internal static readonly Task<int>[] s_int32Tasks = CreateInt32Tasks();

        internal const int InclusiveInt32Min = -1;

        internal const int ExclusiveInt32Max = 9;

        internal static Task<TResult> CreateCacheableTask<TResult>(TResult result)
        {
            return new Task<TResult>(canceled: false, result, (TaskCreationOptions)16384, default(CancellationToken));
        }

        private static Task<int>[] CreateInt32Tasks()
        {
            Task<int>[] array = new Task<int>[10];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = CreateCacheableTask(i + -1);
            }
            return array;
        }
    }


    public class TaskCanceledException : OperationCanceledException
    {
        [NonSerialized]
        private readonly Task _canceledTask;

        public Task? Task => _canceledTask;

        public TaskCanceledException()
            : base(SR.TaskCanceledException_ctor_DefaultMessage)
        {
        }

        public TaskCanceledException(string? message)
            : base(message ?? SR.TaskCanceledException_ctor_DefaultMessage)
        {
        }

        public TaskCanceledException(string? message, Exception? innerException)
            : base(message ?? SR.TaskCanceledException_ctor_DefaultMessage, innerException)
        {
        }

        public TaskCanceledException(string? message, Exception? innerException, CancellationToken token)
            : base(message ?? SR.TaskCanceledException_ctor_DefaultMessage, innerException, token)
        {
        }

        public TaskCanceledException(Task? task)
            : base(SR.TaskCanceledException_ctor_DefaultMessage, task?.CancellationToken ?? CancellationToken.None)
        {
            _canceledTask = task;
        }

    }


    public class TaskCompletionSource
    {
        private readonly Task _task;

        public Task Task => _task;

        public TaskCompletionSource()
        {
            _task = new Task();
        }

        public TaskCompletionSource(TaskCreationOptions creationOptions)
            : this(null, creationOptions)
        {
        }

        public TaskCompletionSource(object? state)
            : this(state, TaskCreationOptions.None)
        {
        }

        public TaskCompletionSource(object? state, TaskCreationOptions creationOptions)
        {
            _task = new Task(state, creationOptions, promiseStyle: true);
        }

        public void SetException(Exception exception)
        {
            if (!TrySetException(exception))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public void SetException(IEnumerable<Exception> exceptions)
        {
            if (!TrySetException(exceptions))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetException(Exception exception)
        {
            if (exception is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exception);
            }
            bool num = _task.TrySetException(exception);
            if (!num && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public bool TrySetException(IEnumerable<Exception> exceptions)
        {
            if (exceptions is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exceptions);
            }
            List<Exception> list = new List<Exception>();
            foreach (Exception exception in exceptions)
            {
                if (exception is null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.TaskCompletionSourceT_TrySetException_NullException, ExceptionArgument.exceptions);
                }
                list.Add(exception);
            }
            if (list.Count == 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.TaskCompletionSourceT_TrySetException_NoExceptions, ExceptionArgument.exceptions);
            }
            bool num = _task.TrySetException(list);
            if (!num && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public void SetResult()
        {
            if (!TrySetResult())
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetResult()
        {
            bool num = _task.TrySetResult();
            if (!num)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public void SetCanceled()
        {
            SetCanceled(default(CancellationToken));
        }

        public void SetCanceled(CancellationToken cancellationToken)
        {
            if (!TrySetCanceled(cancellationToken))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetCanceled()
        {
            return TrySetCanceled(default(CancellationToken));
        }

        public bool TrySetCanceled(CancellationToken cancellationToken)
        {
            bool num = _task.TrySetCanceled(cancellationToken);
            if (!num && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public void SetFromTask(Task completedTask)
        {
            if (!TrySetFromTask(completedTask))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetFromTask(Task completedTask)
        {
            ArgumentNullException.ThrowIfNull(completedTask);
            if (!completedTask.IsCompleted)
            {
                throw new ArgumentException(SR.Task_MustBeCompleted, "completedTask");
            }
            bool flag = false;
            switch (completedTask.Status)
            {
            case TaskStatus.RanToCompletion:
                flag = _task.TrySetResult();
                break;
            case TaskStatus.Canceled:
                flag = _task.TrySetCanceled(completedTask.CancellationToken, completedTask.GetCancellationExceptionDispatchInfo());
                break;
            case TaskStatus.Faulted:
                flag = _task.TrySetException(completedTask.GetExceptionDispatchInfos());
                break;
            }
            if (!flag && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return flag;
        }
    }

    public class TaskCompletionSource<TResult>
    {
        private readonly Task<TResult> _task;

        public Task<TResult> Task => _task;

        public TaskCompletionSource()
        {
            _task = new Task<TResult>();
        }

        public TaskCompletionSource(TaskCreationOptions creationOptions)
            : this((object?)null, creationOptions)
        {
        }

        public TaskCompletionSource(object? state)
            : this(state, TaskCreationOptions.None)
        {
        }

        public TaskCompletionSource(object? state, TaskCreationOptions creationOptions)
        {
            _task = new Task<TResult>(state, creationOptions);
        }

        public void SetException(Exception exception)
        {
            if (!TrySetException(exception))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public void SetException(IEnumerable<Exception> exceptions)
        {
            if (!TrySetException(exceptions))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetException(Exception exception)
        {
            if (exception is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exception);
            }
            bool num = _task.TrySetException(exception);
            if (!num && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public bool TrySetException(IEnumerable<Exception> exceptions)
        {
            if (exceptions is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exceptions);
            }
            List<Exception> list = new List<Exception>();
            foreach (Exception exception in exceptions)
            {
                if (exception is null)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.TaskCompletionSourceT_TrySetException_NullException, ExceptionArgument.exceptions);
                }
                list.Add(exception);
            }
            if (list.Count == 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.TaskCompletionSourceT_TrySetException_NoExceptions, ExceptionArgument.exceptions);
            }
            bool num = _task.TrySetException(list);
            if (!num && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public void SetResult(TResult result)
        {
            if (!TrySetResult(result))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetResult(TResult result)
        {
            bool num = _task.TrySetResult(result);
            if (!num)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public void SetCanceled()
        {
            SetCanceled(default(CancellationToken));
        }

        public void SetCanceled(CancellationToken cancellationToken)
        {
            if (!TrySetCanceled(cancellationToken))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetCanceled()
        {
            return TrySetCanceled(default(CancellationToken));
        }

        public bool TrySetCanceled(CancellationToken cancellationToken)
        {
            bool num = _task.TrySetCanceled(cancellationToken);
            if (!num && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return num;
        }

        public void SetFromTask(Task<TResult> completedTask)
        {
            if (!TrySetFromTask(completedTask))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public bool TrySetFromTask(Task<TResult> completedTask)
        {
            ArgumentNullException.ThrowIfNull(completedTask);
            if (!completedTask.IsCompleted)
            {
                throw new ArgumentException(SR.Task_MustBeCompleted, "completedTask");
            }
            bool flag = false;
            switch (completedTask.Status)
            {
            case TaskStatus.RanToCompletion:
                flag = _task.TrySetResult(completedTask.Result);
                break;
            case TaskStatus.Canceled:
                flag = _task.TrySetCanceled(completedTask.CancellationToken, completedTask.GetCancellationExceptionDispatchInfo());
                break;
            case TaskStatus.Faulted:
                flag = _task.TrySetException(completedTask.GetExceptionDispatchInfos());
                break;
            }
            if (!flag && !_task.IsCompleted)
            {
                _task.SpinUntilCompleted();
            }
            return flag;
        }
    }


    internal abstract class TaskContinuation
    {
        internal abstract void Run(Task completedTask, bool canInlineContinuationTask);

        protected static void InlineIfPossibleOrElseQueue(Task task, bool needsProtection)
        {
            if (needsProtection)
            {
                if (!task.MarkStarted())
                {
                    return;
                }
            }
            else
            {
                task.m_stateFlags |= 65536;
            }
            try
            {
                if (!task.m_taskScheduler.TryRunInline(task, taskWasPreviouslyQueued: false))
                {
                    task.m_taskScheduler.InternalQueueTask(task);
                }
            }
            catch (Exception innerException)
            {
                TaskSchedulerException exceptionObject = new TaskSchedulerException(innerException);
                task.AddException(exceptionObject);
                task.Finish(userDelegateExecute: false);
            }
        }

        internal abstract Delegate[] GetDelegateContinuationsForDebugger();
    }


    internal sealed class TaskExceptionHolder
    {
        private readonly Task m_task;

        private volatile List<ExceptionDispatchInfo> m_faultExceptions;

        private ExceptionDispatchInfo m_cancellationException;

        private volatile bool m_isHandled;

        internal bool ContainsFaultList => m_faultExceptions is not null;

        internal TaskExceptionHolder(Task task)
        {
            m_task = task;
        }

        ~TaskExceptionHolder()
        {
            if (m_faultExceptions is not null && !m_isHandled)
            {
                UnobservedTaskExceptionEventArgs ueea = new UnobservedTaskExceptionEventArgs(new AggregateException(SR.TaskExceptionHolder_UnhandledException, m_faultExceptions));
                TaskScheduler.PublishUnobservedTaskException(m_task, ueea);
            }
        }

        internal void Add(object exceptionObject, bool representsCancellation)
        {
            if (representsCancellation)
            {
                SetCancellationException(exceptionObject);
            }
            else
            {
                AddFaultException(exceptionObject);
            }
        }

        private void SetCancellationException(object exceptionObject)
        {
            if (exceptionObject is OperationCanceledException source)
            {
                m_cancellationException = ExceptionDispatchInfo.Capture(source);
            }
            else
            {
                ExceptionDispatchInfo cancellationException = exceptionObject as ExceptionDispatchInfo;
                m_cancellationException = cancellationException;
            }
            MarkAsHandled(calledFromFinalizer: false);
        }

        private void AddFaultException(object exceptionObject)
        {
            List<ExceptionDispatchInfo> list = m_faultExceptions ?? (m_faultExceptions = new List<ExceptionDispatchInfo>(1));
            if (exceptionObject is Exception source)
            {
                list.Add(ExceptionDispatchInfo.Capture(source));
            }
            else if (exceptionObject is ExceptionDispatchInfo item)
            {
                list.Add(item);
            }
            else if (exceptionObject is IEnumerable<Exception> enumerable)
            {
                foreach (Exception item2 in enumerable)
                {
                    list.Add(ExceptionDispatchInfo.Capture(item2));
                }
            }
            else
            {
                if (!(exceptionObject is IEnumerable<ExceptionDispatchInfo> collection))
                {
                    throw new ArgumentException(SR.TaskExceptionHolder_UnknownExceptionType, "exceptionObject");
                }
                list.AddRange(collection);
            }
            if (list.Count > 0)
            {
                MarkAsUnhandled();
            }
        }

        private void MarkAsUnhandled()
        {
            if (m_isHandled)
            {
                GC.ReRegisterForFinalize(this);
                m_isHandled = false;
            }
        }

        internal void MarkAsHandled(bool calledFromFinalizer)
        {
            if (!m_isHandled)
            {
                if (!calledFromFinalizer)
                {
                    GC.SuppressFinalize(this);
                }
                m_isHandled = true;
            }
        }

        internal AggregateException CreateExceptionObject(bool calledFromFinalizer, Exception includeThisException)
        {
            List<ExceptionDispatchInfo> faultExceptions = m_faultExceptions;
            MarkAsHandled(calledFromFinalizer);
            if (includeThisException is null)
            {
                return new AggregateException(faultExceptions);
            }
            Exception[] array = new Exception[faultExceptions.Count + 1];
            for (int i = 0; i < array.Length - 1; i++)
            {
                array[i] = faultExceptions[i].SourceException;
            }
            array[^1] = includeThisException;
            return new AggregateException(array);
        }

        internal List<ExceptionDispatchInfo> GetExceptionDispatchInfos()
        {
            List<ExceptionDispatchInfo> faultExceptions = m_faultExceptions;
            MarkAsHandled(calledFromFinalizer: false);
            return faultExceptions;
        }

        internal ExceptionDispatchInfo GetCancellationExceptionDispatchInfo()
        {
            return m_cancellationException;
        }
    }


    public static class TaskExtensions
    {
        public static Task Unwrap(this Task<Task> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            object task2;
            if (task.IsCompletedSuccessfully)
            {
                task2 = task.Result;
                if (task2 is null)
                {
                    return Task.FromCanceled(new CancellationToken(canceled: true));
                }
            }
            else
            {
                task2 = Task.CreateUnwrapPromise<VoidTaskResult>(task, lookForOce: false);
            }
            return (Task)task2;
        }

        public static Task<TResult> Unwrap<TResult>(this Task<Task<TResult>> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            Task<TResult> task2;
            if (task.IsCompletedSuccessfully)
            {
                task2 = task.Result;
                if (task2 is null)
                {
                    return Task.FromCanceled<TResult>(new CancellationToken(canceled: true));
                }
            }
            else
            {
                task2 = Task.CreateUnwrapPromise<TResult>(task, lookForOce: false);
            }
            return task2;
        }
    }


    internal sealed class TaskSchedulerAwaitTaskContinuation : AwaitTaskContinuation
    {
        private readonly TaskScheduler m_scheduler;

        internal TaskSchedulerAwaitTaskContinuation(TaskScheduler scheduler, Action action, bool flowExecutionContext)
            : base(action, flowExecutionContext)
        {
            m_scheduler = scheduler;
        }

        internal sealed override void Run(Task ignored, bool canInlineContinuationTask)
        {
            if (m_scheduler == TaskScheduler.Default)
            {
                base.Run(ignored, canInlineContinuationTask);
            }
            else
            {
                RunOrScheduleAction(m_action, m_scheduler, m_capturedContext, canInlineContinuationTask);
            }
        }

        internal static void RunOrScheduleAction(Action action, TaskScheduler scheduler, ExecutionContext capturedContext, bool allowInlining)
        {
            bool num = allowInlining && (TaskScheduler.InternalCurrent == scheduler || Thread.CurrentThread.IsThreadPoolThread);
            Task task = AwaitTaskContinuation.CreateTask(delegate(object state)
            {
                try
                {
                    ((Action)state)();
                }
                catch (Exception exception)
                {
                    Task.ThrowAsync(exception, null);
                }
            }, action, scheduler, capturedContext);
            if (num)
            {
                TaskContinuation.InlineIfPossibleOrElseQueue(task, needsProtection: false);
                return;
            }
            try
            {
                task.ScheduleAndStart(needsProtection: false);
            }
            catch (TaskSchedulerException)
            {
            }
        }
    }


    public class TaskSchedulerException : Exception
    {
        public TaskSchedulerException()
            : base(SR.TaskSchedulerException_ctor_DefaultMessage)
        {
        }

        public TaskSchedulerException(string? message)
            : base(message ?? SR.TaskSchedulerException_ctor_DefaultMessage)
        {
        }

        public TaskSchedulerException(Exception? innerException)
            : base(SR.TaskSchedulerException_ctor_DefaultMessage, innerException)
        {
        }

        public TaskSchedulerException(string? message, Exception? innerException)
            : base(message ?? SR.TaskSchedulerException_ctor_DefaultMessage, innerException)
        {
        }

    }


    public static class TaskToAsyncResult
    {
        private sealed class TaskAsyncResult : IAsyncResult
        {
            internal readonly Task _task;

            private readonly AsyncCallback _callback;

            public object AsyncState { get; }

            public bool CompletedSynchronously { get; }

            public bool IsCompleted => _task.IsCompleted;

            public WaitHandle AsyncWaitHandle => ((IAsyncResult)_task).AsyncWaitHandle;

            internal TaskAsyncResult(Task task, object state, AsyncCallback callback)
            {
                _task = task;
                AsyncState = state;
                if (task.IsCompleted)
                {
                    CompletedSynchronously = true;
                    callback?.Invoke(this);
                }
                else if (callback is not null)
                {
                    _callback = callback;
                    _task.ConfigureAwait(continueOnCapturedContext: false).GetAwaiter().OnCompleted(delegate
                    {
                        _callback(this);
                    });
                }
            }
        }

        public static IAsyncResult Begin(Task task, AsyncCallback? callback, object? state)
        {
            if (OperatingSystem.IsWasi())
            {
                throw new PlatformNotSupportedException();
            }
            ArgumentNullException.ThrowIfNull(task);
            return new TaskAsyncResult(task, state, callback);
        }

        public static void End(IAsyncResult asyncResult)
        {
            Unwrap(asyncResult).GetAwaiter().GetResult();
        }

        public static TResult End<TResult>(IAsyncResult asyncResult)
        {
            return Unwrap<TResult>(asyncResult).GetAwaiter().GetResult();
        }

        public static Task Unwrap(IAsyncResult asyncResult)
        {
            ArgumentNullException.ThrowIfNull(asyncResult);
            return (asyncResult as TaskAsyncResult)?._task ?? throw new ArgumentException(null, "asyncResult");
        }

        public static Task<TResult> Unwrap<TResult>(IAsyncResult asyncResult)
        {
            ArgumentNullException.ThrowIfNull(asyncResult);
            return ((asyncResult as TaskAsyncResult)?._task as Task<TResult>) ?? throw new ArgumentException(null, "asyncResult");
        }
    }


    internal sealed class ThreadPoolTaskScheduler : TaskScheduler
    {
        private static readonly ParameterizedThreadStart s_longRunningThreadWork = delegate(object s)
        {
            ((Task)s).ExecuteEntryUnsafe(null);
        };

        internal ThreadPoolTaskScheduler()
        {
            _ = base.Id;
        }

        protected internal override void QueueTask(Task task)
        {
            TaskCreationOptions options = task.Options;
            if (RuntimeFeature.IsMultithreadingSupported && (options & TaskCreationOptions.LongRunning) != TaskCreationOptions.None)
            {
                Thread thread = new Thread(s_longRunningThreadWork);
                thread.IsBackground = true;
                thread.Name = ".NET Long Task";
                thread.UnsafeStart(task);
            }
            else
            {
                ThreadPool.UnsafeQueueUserWorkItemInternal(task, (options & TaskCreationOptions.PreferFairness) == 0);
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (taskWasPreviouslyQueued && !ThreadPool.TryPopCustomWorkItem(task))
            {
                return false;
            }
            try
            {
                task.ExecuteEntryUnsafe(null);
            }
            finally
            {
                if (taskWasPreviouslyQueued)
                {
                    NotifyWorkItemProgress();
                }
            }
            return true;
        }

        protected internal override bool TryDequeue(Task task)
        {
            return ThreadPool.TryPopCustomWorkItem(task);
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return FilterTasksFromWorkItems(ThreadPool.GetQueuedWorkItems());
        }

        private static IEnumerable<Task> FilterTasksFromWorkItems(IEnumerable<object> tpwItems)
        {
            foreach (object tpwItem in tpwItems)
            {
                if (tpwItem is Task task)
                {
                    yield return task;
                }
            }
        }

        internal override void NotifyWorkItemProgress()
        {
            ThreadPool.NotifyWorkItemProgress();
        }
    }


    public class UnobservedTaskExceptionEventArgs : EventArgs
    {
        private readonly AggregateException m_exception;

        internal bool m_observed;

        public bool Observed => m_observed;

        public AggregateException Exception => m_exception;

        public UnobservedTaskExceptionEventArgs(AggregateException exception)
        {
            m_exception = exception;
        }

        public void SetObserved()
        {
            m_observed = true;
        }
    }

    public class TaskFactory
    {
        private sealed class CompleteOnCountdownPromise : Task<Task[]>, ITaskCompletionAction
        {
            private readonly Task[] _tasks;

            private int _count;

            public bool InvokeMayRunArbitraryCode => true;

            private protected override bool ShouldNotifyDebuggerOfWaitCompletion
            {
                get
                {
                    if (base.ShouldNotifyDebuggerOfWaitCompletion)
                    {
                        return Task.AnyTaskRequiresNotifyDebuggerOfWaitCompletion(_tasks);
                    }
                    return false;
                }
            }

            internal CompleteOnCountdownPromise(Task[] tasksCopy)
            {
                _tasks = tasksCopy;
                _count = tasksCopy.Length;
                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationBegin(base.Id, "TaskFactory.ContinueWhenAll", 0L);
                }
                if (Task.s_asyncDebuggingEnabled)
                {
                    Task.AddToActiveTasks(this);
                }
            }

            public void Invoke(Task completingTask)
            {
                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationRelation(base.Id, CausalityRelation.Join);
                }
                if (completingTask.IsWaitNotificationEnabled)
                {
                    SetNotificationForWaitCompletion(enabled: true);
                }
                if (Interlocked.Decrement(ref _count) == 0)
                {
                    if (TplEventSource.Log.IsEnabled())
                    {
                        TplEventSource.Log.TraceOperationEnd(base.Id, AsyncCausalityStatus.Completed);
                    }
                    if (Task.s_asyncDebuggingEnabled)
                    {
                        Task.RemoveFromActiveTasks(this);
                    }
                    TrySetResult(_tasks);
                }
            }
        }

        private sealed class CompleteOnCountdownPromise<T> : Task<Task<T>[]>, ITaskCompletionAction
        {
            private readonly Task<T>[] _tasks;

            private int _count;

            public bool InvokeMayRunArbitraryCode => true;

            private protected override bool ShouldNotifyDebuggerOfWaitCompletion
            {
                get
                {
                    if (base.ShouldNotifyDebuggerOfWaitCompletion)
                    {
                        Task[] tasks = _tasks;
                        return Task.AnyTaskRequiresNotifyDebuggerOfWaitCompletion(tasks);
                    }
                    return false;
                }
            }

            internal CompleteOnCountdownPromise(Task<T>[] tasksCopy)
            {
                _tasks = tasksCopy;
                _count = tasksCopy.Length;
                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationBegin(base.Id, "TaskFactory.ContinueWhenAll<>", 0L);
                }
                if (Task.s_asyncDebuggingEnabled)
                {
                    Task.AddToActiveTasks(this);
                }
            }

            public void Invoke(Task completingTask)
            {
                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationRelation(base.Id, CausalityRelation.Join);
                }
                if (completingTask.IsWaitNotificationEnabled)
                {
                    SetNotificationForWaitCompletion(enabled: true);
                }
                if (Interlocked.Decrement(ref _count) == 0)
                {
                    if (TplEventSource.Log.IsEnabled())
                    {
                        TplEventSource.Log.TraceOperationEnd(base.Id, AsyncCausalityStatus.Completed);
                    }
                    if (Task.s_asyncDebuggingEnabled)
                    {
                        Task.RemoveFromActiveTasks(this);
                    }
                    TrySetResult(_tasks);
                }
            }
        }

        internal sealed class CompleteOnInvokePromise<TTask> : Task<TTask>, ITaskCompletionAction where TTask : Task
        {
            private const int CompletedFlag = 1;

            private const int SyncBlockingFlag = 2;

            private IList<TTask> _tasks;

            private int _stateFlags;

            public bool InvokeMayRunArbitraryCode => (_stateFlags & 2) == 0;

            public CompleteOnInvokePromise(IList<TTask> tasks, bool isSyncBlocking)
            {
                _tasks = tasks;
                if (isSyncBlocking)
                {
                    _stateFlags = 2;
                }
                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationBegin(base.Id, "TaskFactory.ContinueWhenAny", 0L);
                }
                if (Task.s_asyncDebuggingEnabled)
                {
                    Task.AddToActiveTasks(this);
                }
            }

            public void Invoke(Task completingTask)
            {
                int stateFlags = _stateFlags;
                int num = stateFlags & 2;
                if ((stateFlags & 1) != 0 || Interlocked.Exchange(ref _stateFlags, num | 1) != num)
                {
                    return;
                }
                if (TplEventSource.Log.IsEnabled())
                {
                    TplEventSource.Log.TraceOperationRelation(base.Id, CausalityRelation.Choice);
                    TplEventSource.Log.TraceOperationEnd(base.Id, AsyncCausalityStatus.Completed);
                }
                if (Task.s_asyncDebuggingEnabled)
                {
                    Task.RemoveFromActiveTasks(this);
                }
                TrySetResult((TTask)completingTask);
                IList<TTask> tasks = _tasks;
                int count = tasks.Count;
                for (int i = 0; i < count; i++)
                {
                    TTask val = tasks[i];
                    if (val is not null && !val.IsCompleted)
                    {
                        val.RemoveContinuation(this);
                    }
                }
                _tasks = null;
            }
        }

        private readonly CancellationToken m_defaultCancellationToken;

        private readonly TaskScheduler m_defaultScheduler;

        private readonly TaskCreationOptions m_defaultCreationOptions;

        private readonly TaskContinuationOptions m_defaultContinuationOptions;

        private TaskScheduler DefaultScheduler => m_defaultScheduler ?? TaskScheduler.Current;

        public CancellationToken CancellationToken => m_defaultCancellationToken;

        public TaskScheduler? Scheduler => m_defaultScheduler;

        public TaskCreationOptions CreationOptions => m_defaultCreationOptions;

        public TaskContinuationOptions ContinuationOptions => m_defaultContinuationOptions;

        private TaskScheduler GetDefaultScheduler(Task currTask)
        {
            TaskScheduler? taskScheduler = m_defaultScheduler;
            if (taskScheduler is null)
            {
                if (currTask is null || (currTask.CreationOptions & TaskCreationOptions.HideScheduler) != TaskCreationOptions.None)
                {
                    return TaskScheduler.Default;
                }
                taskScheduler = currTask.ExecutingTaskScheduler;
            }
            return taskScheduler;
        }

        public TaskFactory()
        {
        }

        public TaskFactory(CancellationToken cancellationToken)
        {
            m_defaultCancellationToken = cancellationToken;
        }

        public TaskFactory(TaskScheduler? scheduler)
        {
            m_defaultScheduler = scheduler;
        }

        public TaskFactory(TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions)
        {
            CheckMultiTaskContinuationOptions(continuationOptions);
            CheckCreationOptions(creationOptions);
            m_defaultCreationOptions = creationOptions;
            m_defaultContinuationOptions = continuationOptions;
        }

        public TaskFactory(CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions, TaskScheduler? scheduler)
            : this(creationOptions, continuationOptions)
        {
            m_defaultCancellationToken = cancellationToken;
            m_defaultScheduler = scheduler;
        }

        internal static void CheckCreationOptions(TaskCreationOptions creationOptions)
        {
            if ((creationOptions & ~(TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent | TaskCreationOptions.DenyChildAttach | TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously)) != TaskCreationOptions.None)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.creationOptions);
            }
        }

        public Task StartNew(Action action)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task.InternalStartNew(internalCurrent, action, null, m_defaultCancellationToken, GetDefaultScheduler(internalCurrent), m_defaultCreationOptions, InternalTaskOptions.None);
        }

        public Task StartNew(Action action, CancellationToken cancellationToken)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task.InternalStartNew(internalCurrent, action, null, cancellationToken, GetDefaultScheduler(internalCurrent), m_defaultCreationOptions, InternalTaskOptions.None);
        }

        public Task StartNew(Action action, TaskCreationOptions creationOptions)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task.InternalStartNew(internalCurrent, action, null, m_defaultCancellationToken, GetDefaultScheduler(internalCurrent), creationOptions, InternalTaskOptions.None);
        }

        public Task StartNew(Action action, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return Task.InternalStartNew(Task.InternalCurrentIfAttached(creationOptions), action, null, cancellationToken, scheduler, creationOptions, InternalTaskOptions.None);
        }

        public Task StartNew(Action<object?> action, object? state)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task.InternalStartNew(internalCurrent, action, state, m_defaultCancellationToken, GetDefaultScheduler(internalCurrent), m_defaultCreationOptions, InternalTaskOptions.None);
        }

        public Task StartNew(Action<object?> action, object? state, CancellationToken cancellationToken)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task.InternalStartNew(internalCurrent, action, state, cancellationToken, GetDefaultScheduler(internalCurrent), m_defaultCreationOptions, InternalTaskOptions.None);
        }

        public Task StartNew(Action<object?> action, object? state, TaskCreationOptions creationOptions)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task.InternalStartNew(internalCurrent, action, state, m_defaultCancellationToken, GetDefaultScheduler(internalCurrent), creationOptions, InternalTaskOptions.None);
        }

        public Task StartNew(Action<object?> action, object? state, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return Task.InternalStartNew(Task.InternalCurrentIfAttached(creationOptions), action, state, cancellationToken, scheduler, creationOptions, InternalTaskOptions.None);
        }

        public Task<TResult> StartNew<TResult>(Func<TResult> function)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task<TResult>.StartNew(internalCurrent, function, m_defaultCancellationToken, m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(internalCurrent));
        }

        public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task<TResult>.StartNew(internalCurrent, function, cancellationToken, m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(internalCurrent));
        }

        public Task<TResult> StartNew<TResult>(Func<TResult> function, TaskCreationOptions creationOptions)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task<TResult>.StartNew(internalCurrent, function, m_defaultCancellationToken, creationOptions, InternalTaskOptions.None, GetDefaultScheduler(internalCurrent));
        }

        public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return Task<TResult>.StartNew(Task.InternalCurrentIfAttached(creationOptions), function, cancellationToken, creationOptions, InternalTaskOptions.None, scheduler);
        }

        public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task<TResult>.StartNew(internalCurrent, function, state, m_defaultCancellationToken, m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(internalCurrent));
        }

        public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state, CancellationToken cancellationToken)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task<TResult>.StartNew(internalCurrent, function, state, cancellationToken, m_defaultCreationOptions, InternalTaskOptions.None, GetDefaultScheduler(internalCurrent));
        }

        public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state, TaskCreationOptions creationOptions)
        {
            Task internalCurrent = Task.InternalCurrent;
            return Task<TResult>.StartNew(internalCurrent, function, state, m_defaultCancellationToken, creationOptions, InternalTaskOptions.None, GetDefaultScheduler(internalCurrent));
        }

        public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return Task<TResult>.StartNew(Task.InternalCurrentIfAttached(creationOptions), function, state, cancellationToken, creationOptions, InternalTaskOptions.None, scheduler);
        }

        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod)
        {
            return FromAsync(asyncResult, endMethod, m_defaultCreationOptions, DefaultScheduler);
        }

        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions)
        {
            return FromAsync(asyncResult, endMethod, creationOptions, DefaultScheduler);
        }

        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return TaskFactory<VoidTaskResult>.FromAsyncImpl(asyncResult, null, endMethod, creationOptions, scheduler);
        }

        public Task FromAsync(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object? state)
        {
            return FromAsync(beginMethod, endMethod, state, m_defaultCreationOptions);
        }

        public Task FromAsync(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<VoidTaskResult>.FromAsyncImpl(beginMethod, null, endMethod, state, creationOptions);
        }

        public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, object? state)
        {
            return FromAsync(beginMethod, endMethod, arg1, state, m_defaultCreationOptions);
        }

        public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<VoidTaskResult>.FromAsyncImpl(beginMethod, null, endMethod, arg1, state, creationOptions);
        }

        public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object? state)
        {
            return FromAsync(beginMethod, endMethod, arg1, arg2, state, m_defaultCreationOptions);
        }

        public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<VoidTaskResult>.FromAsyncImpl(beginMethod, null, endMethod, arg1, arg2, state, creationOptions);
        }

        public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state)
        {
            return FromAsync(beginMethod, endMethod, arg1, arg2, arg3, state, m_defaultCreationOptions);
        }

        public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<VoidTaskResult>.FromAsyncImpl(beginMethod, null, endMethod, arg1, arg2, arg3, state, creationOptions);
        }

        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod)
        {
            return TaskFactory<TResult>.FromAsyncImpl(asyncResult, endMethod, null, m_defaultCreationOptions, DefaultScheduler);
        }

        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions)
        {
            return TaskFactory<TResult>.FromAsyncImpl(asyncResult, endMethod, null, creationOptions, DefaultScheduler);
        }

        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler)
        {
            return TaskFactory<TResult>.FromAsyncImpl(asyncResult, endMethod, null, creationOptions, scheduler);
        }

        public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object? state)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, state, m_defaultCreationOptions);
        }

        public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, state, creationOptions);
        }

        public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object? state)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, arg1, state, m_defaultCreationOptions);
        }

        public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, arg1, state, creationOptions);
        }

        public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object? state)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, state, m_defaultCreationOptions);
        }

        public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, state, creationOptions);
        }

        public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, arg3, state, m_defaultCreationOptions);
        }

        public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state, TaskCreationOptions creationOptions)
        {
            return TaskFactory<TResult>.FromAsyncImpl(beginMethod, endMethod, null, arg1, arg2, arg3, state, creationOptions);
        }

        internal static void CheckFromAsyncOptions(TaskCreationOptions creationOptions, bool hasBeginMethod)
        {
            if (hasBeginMethod)
            {
                if ((creationOptions & TaskCreationOptions.LongRunning) != TaskCreationOptions.None)
                {
                    throw new ArgumentOutOfRangeException(nameof(creationOptions), SR.Task_FromAsync_LongRunning);
                }
                if ((creationOptions & TaskCreationOptions.PreferFairness) != TaskCreationOptions.None)
                {
                    throw new ArgumentOutOfRangeException(nameof(creationOptions), SR.Task_FromAsync_PreferFairness);
                }
            }
            if ((creationOptions & ~(TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent | TaskCreationOptions.DenyChildAttach | TaskCreationOptions.HideScheduler)) != TaskCreationOptions.None)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.creationOptions);
            }
        }

        internal static Task<Task[]> CommonCWAllLogic(Task[] tasksCopy)
        {
            CompleteOnCountdownPromise completeOnCountdownPromise = new CompleteOnCountdownPromise(tasksCopy);
            for (int i = 0; i < tasksCopy.Length; i++)
            {
                if (tasksCopy[i].IsCompleted)
                {
                    completeOnCountdownPromise.Invoke(tasksCopy[i]);
                }
                else
                {
                    tasksCopy[i].AddCompletionAction(completeOnCountdownPromise);
                }
            }
            return completeOnCountdownPromise;
        }

        internal static Task<Task<T>[]> CommonCWAllLogic<T>(Task<T>[] tasksCopy)
        {
            CompleteOnCountdownPromise<T> completeOnCountdownPromise = new CompleteOnCountdownPromise<T>(tasksCopy);
            for (int i = 0; i < tasksCopy.Length; i++)
            {
                if (tasksCopy[i].IsCompleted)
                {
                    completeOnCountdownPromise.Invoke(tasksCopy[i]);
                }
                else
                {
                    tasksCopy[i].AddCompletionAction(completeOnCountdownPromise);
                }
            }
            return completeOnCountdownPromise;
        }

        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, continuationOptions, cancellationToken, scheduler);
        }

        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAllImpl(tasks, null, continuationAction, continuationOptions, cancellationToken, scheduler);
        }

        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }

        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAllImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }

        internal static Task<TTask> CommonCWAnyLogic<TTask>(IList<TTask> tasks, bool isSyncBlocking = false) where TTask : Task
        {
            CompleteOnInvokePromise<TTask> completeOnInvokePromise = new CompleteOnInvokePromise<TTask>(tasks, isSyncBlocking);
            bool flag = false;
            int count = tasks.Count;
            for (int i = 0; i < count; i++)
            {
                Task task = tasks[i] ?? throw new ArgumentException(SR.Task_MultiTaskContinuation_NullTask, "tasks");
                if (flag)
                {
                    continue;
                }
                if (completeOnInvokePromise.IsCompleted)
                {
                    flag = true;
                    continue;
                }
                if (task.IsCompleted)
                {
                    completeOnInvokePromise.Invoke(task);
                    flag = true;
                    continue;
                }
                task.AddCompletionAction(completeOnInvokePromise, isSyncBlocking);
                if (completeOnInvokePromise.IsCompleted)
                {
                    task.RemoveContinuation(completeOnInvokePromise);
                }
            }
            return completeOnInvokePromise;
        }

        internal static void CommonCWAnyLogicCleanup(Task<Task> continuation)
        {
            ((CompleteOnInvokePromise<Task>)continuation).Invoke(null);
        }

        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, continuationOptions, cancellationToken, scheduler);
        }

        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }

        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            return TaskFactory<TResult>.ContinueWhenAnyImpl(tasks, continuationFunction, null, continuationOptions, cancellationToken, scheduler);
        }

        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, m_defaultContinuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, m_defaultContinuationOptions, cancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, continuationOptions, m_defaultCancellationToken, DefaultScheduler);
        }

        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            return TaskFactory<VoidTaskResult>.ContinueWhenAnyImpl(tasks, null, continuationAction, continuationOptions, cancellationToken, scheduler);
        }

        internal static Task[] CheckMultiContinuationTasksAndCopy(Task[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            if (tasks.Length == 0)
            {
                throw new ArgumentException(SR.Task_MultiTaskContinuation_EmptyTaskList, "tasks");
            }
            Task[] array = new Task[tasks.Length];
            for (int i = 0; i < tasks.Length; i++)
            {
                array[i] = tasks[i];
                if (array[i] is null)
                {
                    throw new ArgumentException(SR.Task_MultiTaskContinuation_NullTask, "tasks");
                }
            }
            return array;
        }

        internal static Task<TResult>[] CheckMultiContinuationTasksAndCopy<TResult>(Task<TResult>[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            if (tasks.Length == 0)
            {
                throw new ArgumentException(SR.Task_MultiTaskContinuation_EmptyTaskList, "tasks");
            }
            Task<TResult>[] array = new Task<TResult>[tasks.Length];
            for (int i = 0; i < tasks.Length; i++)
            {
                array[i] = tasks[i];
                if (array[i] is null)
                {
                    throw new ArgumentException(SR.Task_MultiTaskContinuation_NullTask, "tasks");
                }
            }
            return array;
        }

        internal static void CheckMultiTaskContinuationOptions(TaskContinuationOptions continuationOptions)
        {
            if ((continuationOptions & (TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously)) == (TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously))
            {
                throw new ArgumentOutOfRangeException(nameof(continuationOptions), SR.Task_ContinueWith_ESandLR);
            }
            if ((continuationOptions & ~(TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning | TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.HideScheduler | TaskContinuationOptions.LazyCancellation | TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously)) != TaskContinuationOptions.None)
            {
                throw new ArgumentOutOfRangeException(nameof(continuationOptions));
            }
            if ((continuationOptions & (TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.NotOnRanToCompletion)) != TaskContinuationOptions.None)
            {
                throw new ArgumentOutOfRangeException(nameof(continuationOptions), SR.Task_MultiTaskContinuation_FireOptions);
            }
        }
    }
}

namespace System.Threading.Tasks.Sources
{

    internal sealed class CapturedSchedulerAndExecutionContext
    {
        internal readonly object _scheduler;

        internal readonly ExecutionContext _executionContext;

        public CapturedSchedulerAndExecutionContext(object scheduler, ExecutionContext executionContext)
        {
            _scheduler = scheduler;
            _executionContext = executionContext;
        }
    }


    public interface IValueTaskSource
    {
        ValueTaskSourceStatus GetStatus(short token);

        void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags);

        void GetResult(short token);
    }

    public interface IValueTaskSource<out TResult>
    {
        ValueTaskSourceStatus GetStatus(short token);

        void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags);

        TResult GetResult(short token);
    }


    [StructLayout(LayoutKind.Auto)]
    public struct ManualResetValueTaskSourceCore<TResult>
    {
        private Action<object> _continuation;

        private object _continuationState;

        private object _capturedContext;

        private ExceptionDispatchInfo _error;

        private TResult _result;

        private short _version;

        private bool _runContinuationsAsynchronously;

        public bool RunContinuationsAsynchronously
        {
            get
            {
                return _runContinuationsAsynchronously;
            }
            set
            {
                _runContinuationsAsynchronously = value;
            }
        }

        public short Version => _version;

        internal bool IsCompleted => (object)Volatile.Read(in _continuation) == ManualResetValueTaskSourceCoreShared.s_sentinel;

        internal object? ContinuationForDiagnostics => _continuationState;

        public void Reset()
        {
            _version++;
            _continuation = null;
            _continuationState = null;
            _capturedContext = null;
            _error = null;
            _result = default(TResult);
        }

        public void SetResult(TResult result)
        {
            _result = result;
            SignalCompletion();
        }

        public void SetException(Exception error)
        {
            _error = ExceptionDispatchInfo.Capture(error);
            SignalCompletion();
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            ValidateToken(token);
            if (IsCompleted)
            {
                if (_error is not null)
                {
                    if (!(_error.SourceException is OperationCanceledException))
                    {
                        return ValueTaskSourceStatus.Faulted;
                    }
                    return ValueTaskSourceStatus.Canceled;
                }
                return ValueTaskSourceStatus.Succeeded;
            }
            return ValueTaskSourceStatus.Pending;
        }

        public TResult GetResult(short token)
        {
            if (token != _version || !IsCompleted || _error is not null)
            {
                ThrowForFailedGetResult();
            }
            return _result;
        }

        private void ThrowForFailedGetResult()
        {
            _error?.Throw();
            throw new InvalidOperationException();
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if (continuation is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.continuation);
            }
            ValidateToken(token);
            if (_continuationState is not null)
            {
                ThrowHelper.ThrowInvalidOperationException();
            }
            _continuationState = state;
            if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != ValueTaskSourceOnCompletedFlags.None)
            {
                _capturedContext = ExecutionContext.Capture();
            }
            if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != ValueTaskSourceOnCompletedFlags.None)
            {
                SynchronizationContext current = SynchronizationContext.Current;
                if (current is not null && current.GetType() != typeof(SynchronizationContext))
                {
                    _capturedContext = ((_capturedContext is null) ? ((object)current) : ((object)new CapturedSchedulerAndExecutionContext(current, (ExecutionContext)_capturedContext)));
                }
                else
                {
                    TaskScheduler current2 = TaskScheduler.Current;
                    if (current2 != TaskScheduler.Default)
                    {
                        _capturedContext = ((_capturedContext is null) ? ((object)current2) : ((object)new CapturedSchedulerAndExecutionContext(current2, (ExecutionContext)_capturedContext)));
                    }
                }
            }
            Action<object> action = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (action is null)
            {
                return;
            }
            _continuationState = null;
            object capturedContext = _capturedContext;
            _capturedContext = null;
            if ((object)action != ManualResetValueTaskSourceCoreShared.s_sentinel)
            {
                ThrowHelper.ThrowInvalidOperationException();
            }
            if (capturedContext is not null)
            {
                if (capturedContext is ExecutionContext)
                {
                    ThreadPool.QueueUserWorkItem<object>(continuation, state, preferLocal: true);
                }
                else
                {
                    ManualResetValueTaskSourceCoreShared.ScheduleCapturedContext(capturedContext, continuation, state);
                }
            }
            else
            {
                ThreadPool.UnsafeQueueUserWorkItem<object>(continuation, state, preferLocal: true);
            }
        }

        private void ValidateToken(short token)
        {
            if (token != _version)
            {
                ThrowHelper.ThrowInvalidOperationException();
            }
        }

        private void SignalCompletion()
        {
            if (IsCompleted)
            {
                ThrowHelper.ThrowInvalidOperationException();
            }
            Action<object> action = Interlocked.Exchange(ref _continuation, ManualResetValueTaskSourceCoreShared.s_sentinel);
            if (action is null)
            {
                return;
            }
            object continuationState = _continuationState;
            _continuationState = null;
            object capturedContext = _capturedContext;
            _capturedContext = null;
            if (capturedContext is null)
            {
                if (_runContinuationsAsynchronously)
                {
                    ThreadPool.UnsafeQueueUserWorkItem(action, continuationState, preferLocal: true);
                }
                else
                {
                    action(continuationState);
                }
            }
            else if ((capturedContext is ExecutionContext || capturedContext is CapturedSchedulerAndExecutionContext) ? true : false)
            {
                ManualResetValueTaskSourceCoreShared.InvokeContinuationWithContext(capturedContext, action, continuationState, _runContinuationsAsynchronously);
            }
            else
            {
                ManualResetValueTaskSourceCoreShared.ScheduleCapturedContext(capturedContext, action, continuationState);
            }
        }
    }


    internal static class ManualResetValueTaskSourceCoreShared
    {
        internal static readonly Action<object> s_sentinel = CompletionSentinel;

        private static void CompletionSentinel(object _)
        {
            ThrowHelper.ThrowInvalidOperationException();
        }

        internal static void ScheduleCapturedContext(object context, Action<object> continuation, object state)
        {
            if (!(context is SynchronizationContext sc))
            {
                if (context is TaskScheduler scheduler)
                {
                    ScheduleTaskScheduler(scheduler, continuation, state);
                    return;
                }
                CapturedSchedulerAndExecutionContext capturedSchedulerAndExecutionContext = (CapturedSchedulerAndExecutionContext)context;
                if (capturedSchedulerAndExecutionContext._scheduler is SynchronizationContext sc2)
                {
                    ScheduleSynchronizationContext(sc2, continuation, state);
                }
                else
                {
                    ScheduleTaskScheduler((TaskScheduler)capturedSchedulerAndExecutionContext._scheduler, continuation, state);
                }
            }
            else
            {
                ScheduleSynchronizationContext(sc, continuation, state);
            }
            static void ScheduleSynchronizationContext(SynchronizationContext synchronizationContext, Action<object> action, object state2)
            {
                synchronizationContext.Post(action.Invoke, state2);
            }
            static void ScheduleTaskScheduler(TaskScheduler scheduler2, Action<object> action, object state2)
            {
                Task.Factory.StartNew(action, state2, CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler2);
            }
        }

        internal static void InvokeContinuationWithContext(object capturedContext, Action<object> continuation, object continuationState, bool runContinuationsAsynchronously)
        {
            ExecutionContext executionContext = ExecutionContext.CaptureForRestore();
            if (capturedContext is ExecutionContext executionContext2)
            {
                ExecutionContext.RestoreInternal(executionContext2);
                if (runContinuationsAsynchronously)
                {
                    try
                    {
                        ThreadPool.QueueUserWorkItem(continuation, continuationState, preferLocal: true);
                        return;
                    }
                    finally
                    {
                        ExecutionContext.RestoreInternal(executionContext);
                    }
                }
                ExceptionDispatchInfo exceptionDispatchInfo = null;
                SynchronizationContext current = SynchronizationContext.Current;
                try
                {
                    continuation(continuationState);
                }
                catch (Exception source)
                {
                    exceptionDispatchInfo = ExceptionDispatchInfo.Capture(source);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(current);
                    ExecutionContext.RestoreInternal(executionContext);
                }
                exceptionDispatchInfo?.Throw();
                return;
            }
            ExecutionContext.Restore(((CapturedSchedulerAndExecutionContext)capturedContext)._executionContext);
            try
            {
                ScheduleCapturedContext(capturedContext, continuation, continuationState);
            }
            finally
            {
                ExecutionContext.RestoreInternal(executionContext);
            }
        }
    }


    [Flags]
    public enum ValueTaskSourceOnCompletedFlags
    {
        None = 0,
        UseSchedulingContext = 1,
        FlowExecutionContext = 2
    }


    public enum ValueTaskSourceStatus
    {
        Pending,
        Succeeded,
        Faulted,
        Canceled
    }


    internal static class LoggingExtensions
    {
        internal static string GetMethodName(this Delegate method) => method.GetType().ToString();
    }
}
