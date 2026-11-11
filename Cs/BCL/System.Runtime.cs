using Internal.Runtime;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace System.Runtime
{
    internal enum RhFailFastReason
    {
        Unknown = 0,
        InternalError = 1,                                   // "Runtime internal error"
        UnhandledException = 2,                              // "unhandled exception"
        UnhandledExceptionFromPInvoke = 3,                   // "Unhandled exception: an unmanaged exception was thrown out of a managed-to-native transition."
        EnvironmentFailFast = 4,
        AssertionFailure = 5,
    }

    // Exposed in Internal.CompilerServices only
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    internal sealed class RuntimeImportAttribute : Attribute
    {
        public string DllName { get; }
        public string EntryPoint { get; }

        public RuntimeImportAttribute(string entry)
        {
            EntryPoint = entry;
        }

        public RuntimeImportAttribute(string dllName, string entry)
        {
            EntryPoint = entry;
            DllName = dllName;
        }
    }

    // Exposed in Internal.CompilerServices only
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public string EntryPoint { get; }

        public RuntimeExportAttribute(string entry)
        {
            EntryPoint = entry;
        }
    }

    internal static class InternalCalls
    {
        private const string RuntimeLibrary = "*";

        //
        // internal calls for allocation
        //
        [RuntimeImport(RuntimeLibrary, "RhpNewFast")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern unsafe object RhpNewFast(MethodTable* pEEType);  // BEWARE: not for finalizable objects!

        //
        // internal calls for finalization
        //
        [RuntimeImport(RuntimeLibrary, "RhpGetNextFinalizableObject")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern object? RhpGetNextFinalizableObject();

        [RuntimeImport(RuntimeLibrary, "RhpFallbackFailFast")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void RhpFallbackFailFast(int code);

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhBulkMoveWithWriteBarrier")]
        internal static extern unsafe void RhBulkMoveWithWriteBarrier(ref byte dmem, ref byte smem, nuint size);
    }

    internal static class RuntimeExports
    {
        [RuntimeExport("RhBoxAny")]
        public static unsafe object RhBoxAny(ref byte data, MethodTable* pEEType)
        {
            if (pEEType->IsValueType)
            {
                return RhBox(pEEType, ref data);
            }
            else
            {
                return Unsafe.As<byte, object>(ref data);
            }
        }

        [RuntimeExport("RhBox")]
        public static unsafe object RhBox(MethodTable* pEEType, ref byte data)
        {
            // A null can be passed for boxing of a null ref.
            _ = Unsafe.ReadUnaligned<byte>(ref data);

            ref byte dataAdjustedForNullable = ref data;

            // If we're boxing a Nullable<T> then either box the underlying T or return null (if the
            // nullable's value is empty).
            if (pEEType->IsNullable)
            {
                // The boolean which indicates whether the value is null comes first in the Nullable struct.
                if (data == 0)
                    return null;

                // Switch type we're going to box to the Nullable<T> target type and advance the data pointer
                // to the value embedded within the nullable.
                dataAdjustedForNullable = ref Unsafe.Add(ref data, pEEType->NullableValueOffset);
                pEEType = pEEType->NullableType;
            }

            object result;
#if FEATURE_64BIT_ALIGNMENT
            if (pEEType->RequiresAlign8)
            {
                result = InternalCalls.RhpNewFastMisalign(pEEType);
            }
            else
#endif // FEATURE_64BIT_ALIGNMENT
            {
                result = InternalCalls.RhpNewFast(pEEType);
            }

            // Copy the unboxed value type data into the new object.
            // Perform any write barriers necessary for embedded reference fields.
            if (pEEType->ContainsGCPointers)
            {
                InternalCalls.RhBulkMoveWithWriteBarrier(ref result.GetRawData(), ref dataAdjustedForNullable, pEEType->ValueTypeSize);
            }
            else
            {
                Unsafe.CopyBlock(ref result.GetRawData(), ref dataAdjustedForNullable, pEEType->ValueTypeSize);
            }

            return result;
        }
    }

    internal static class RuntimeImports
    {
        internal const string RuntimeLibrary = "*";

        // Moves memory from smem to dmem. Size must be a positive value.
        // This copy uses an intrinsic to be safe for copying arbitrary bits of
        // heap memory
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhBulkMoveWithWriteBarrier")]
        internal static extern unsafe void RhBulkMoveWithWriteBarrier(ref byte dmem, ref byte smem, nuint size);

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static unsafe T[] RhAllocateNewArray<T>(int length, uint flags) => null!;

        //
        // calls to runtime for allocation
        // These calls are needed in types which cannot use "new" to allocate and need to do it manually
        //
        // calls to runtime for allocation
        //
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhBoxAny")]
        internal static extern unsafe object RhBoxAny(ref byte pData, MethodTable* pEEType);

        // Those are not standard:

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string RhGetTypeName(IntPtr handle) => null!;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string? RhGetTypeNamespace(IntPtr handle) => null;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string? RhGetTypeFullName(IntPtr handle) => null;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string RhGetTypeDisplayName(IntPtr handle) => null!;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string RhGetTypeAssemblyName(IntPtr handle) => null!;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeFlags(IntPtr handle) => 0;

        // An enum's literals sorted by their value, zero-extended from the enum's storage
        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeEnumCount(IntPtr handle) => 0;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static ulong RhGetTypeEnumValue(IntPtr handle, int index) => 0;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string RhGetTypeEnumName(IntPtr handle, int index) => null!;

        // The register VM has no MethodTables; its importer sends Array.Rank here
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetArrayRank(Array array) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeCorElementType(IntPtr handle) => 0;

        // The register VM's importer sends MethodTable reads here, as it has no MethodTables
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeElementType(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeValueTypeSize(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static bool RhGetTypeContainsGCPointers(IntPtr handle) => false;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static IntPtr RhGetTypeNullableType(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeNullableValueOffset(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetArrayLength(Array array, int dimension) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetArrayLowerBound(Array array, int dimension) => 0;

        internal static int RhMultiplyArrayLengths(int total, int length, int lowerBound)
        {
            if (length < 0)
                throw new OverflowException();
            if ((long)lowerBound + length > (long)int.MaxValue + 1)
                throw new ArgumentOutOfRangeException(nameof(lowerBound));
            ulong product = (ulong)(uint)total * (uint)length;
            if (product > uint.MaxValue)
                throw new OutOfMemoryException();
            return unchecked((int)product);
        }

        internal static int RhValidateArrayLength(int total, bool dimensionTooLong)
        {
            if (total < 0 || dimensionTooLong)
                throw new OutOfMemoryException();
            return total;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static bool RhGetTypeIsFinalizable(IntPtr handle) => false;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static bool RhGetTypeHasComponentSize(IntPtr handle) => false;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static IntPtr RhGetTypeFinalizerCode(IntPtr handle) => 0;

        //
        // calls for GCHandle.
        // These methods are needed to implement GCHandle class like functionality (weak, pinned, etc)
        //
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhpHandleAlloc")]
        private static extern IntPtr RhpHandleAlloc(object? value, GCHandleType type);

        internal static IntPtr RhHandleAlloc(object? value, GCHandleType type)
        {
            IntPtr h = RhpHandleAlloc(value, type);
            if (h == IntPtr.Zero)
                throw new OutOfMemoryException();
            return h;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhpHandleAllocDependent")]
        private static extern IntPtr RhpHandleAllocDependent(object? primary, object? secondary);

        internal static IntPtr RhHandleAllocDependent(object? primary, object? secondary)
        {
            IntPtr h = RhpHandleAllocDependent(primary, secondary);
            if (h == IntPtr.Zero)
                throw new OutOfMemoryException();
            return h;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhHandleFree")]
        internal static extern void RhHandleFree(IntPtr handle);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhHandleGet")]
        internal static extern object? RhHandleGet(IntPtr handle);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhHandleSet")]
        internal static extern void RhHandleSet(IntPtr handle, object? value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhHandleGetDependent")]
        internal static extern object? RhHandleGetDependent(IntPtr handle, out object? secondary);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhHandleSetDependentSecondary")]
        internal static extern void RhHandleSetDependentSecondary(IntPtr handle, object? secondary);

        //
        // calls to GC
        // These methods are needed to implement System.GC like functionality (optimal for finalizers, etc)
        //
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhSuppressFinalize")]
        internal static extern void RhSuppressFinalize(object obj);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhReRegisterForFinalize")]
        internal static extern void RhReRegisterForFinalize(object obj);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhCollect")]
        internal static extern void RhCollect(int generation, InternalGCCollectionMode mode);

        // The object's identity hash, assigned on first request and stable for its lifetime
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetObjectHashCode(object obj) => 0;

        // The identity hash if one was already assigned, or 0
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhTryGetObjectHashCode(object obj) => 0;
    }

    // We choose this name to avoid clashing with any future public class with the name Finalizer.
    internal static class __Finalizer
    {
        private const int UnhandledExceptionInFinalizer = 156;

        [UnmanagedCallersOnly(EntryPoint = "ProcessFinalizers")]
        public static void ProcessFinalizers()
        {
            while (true)
            {
                // Wait until there's some work to be done. If true is returned we should finalize objects,
                // otherwise memory is low and we should initiate a collection.
                /*
                if (InternalCalls.RhpWaitForFinalizerRequest() != 0)
                {
                    int observedFullGcCount = RuntimeImports.RhGetGcCollectionCount(RuntimeImports.RhGetMaxGcGeneration(), false);
                    uint finalizerCount = DrainQueue();

                    // Anyone waiting to drain the Q can now wake up.  Note that there is a
                    // race in that another thread starting a drain, as we leave a drain, may
                    // consider itself satisfied by the drain that just completed.
                    // Thus we include the Full GC count that we have certaily observed.
                    InternalCalls.RhpSignalFinalizationComplete(finalizerCount, observedFullGcCount);
                }
                else
                {
                    // RhpWaitForFinalizerRequest() returned false and indicated that memory is low. We help
                    // out by initiating a garbage collection and then go back to waiting for another request.
                    InternalCalls.RhCollect(0, InternalGCCollectionMode.Blocking, lowMemoryP: Interop.BOOL.TRUE);
                }
                 */
                throw new NotImplementedException();
            }
        }

        // Do not inline this method -- we do not want to accidentally have any temps in ProcessFinalizers which contain
        // objects that came off of the finalizer queue.  If such temps were reported across the duration of the
        // finalizer thread wait operation, it could cause unpredictable behavior with weak handles.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static unsafe uint DrainQueue()
        {
            uint finalizerCount = 0;
            // Drain the queue of finalizable objects.
            while (true)
            {
                object target = InternalCalls.RhpGetNextFinalizableObject();
                if (target == null)
                    return finalizerCount;

                finalizerCount++;

                try
                {
                    // Call the finalizer on the current target object.
                    ((delegate*<object, void>)target.GetMethodTable()->FinalizerCode)(target);
                }
                catch (Exception ex) when (ExceptionHandling.IsHandledByGlobalHandler(ex))
                {
                    // the handler returned "true" means the exception is now "handled" and we should continue.
                }
            }
        }

        // Until there is a finalizer thread an escaping exception ends here and not in the interrupted code
        internal static void DrainQueueOnCurrentThread()
        {
            try
            {
                DrainQueue();
            }
            catch (Exception)
            {
                InternalCalls.RhpFallbackFailFast(UnhandledExceptionInFinalizer);
            }
        }
    }

    /// <summary>
    /// Represents a dependent GC handle, which will conditionally keep a dependent object instance alive as long as
    /// a target object instance is alive as well, without representing a strong reference to the target instance.
    /// </summary>
    public struct DependentHandle : IDisposable
    {
        private IntPtr _handle;

        public DependentHandle(object? target, object? dependent) => _handle = InternalAlloc(target, dependent);

        public readonly bool IsAllocated => _handle != 0;

        public object? Target
        {
            readonly get
            {
                IntPtr handle = _handle;

                if (handle == 0)
                    ThrowHelper.ThrowInvalidOperationException();

                return InternalGetTarget(handle);
            }
            set
            {
                IntPtr handle = _handle;

                if (handle == 0 || value is not null)
                    ThrowHelper.ThrowInvalidOperationException();

                InternalSetTargetToNull(handle);
            }
        }

        public object? Dependent
        {
            readonly get
            {
                IntPtr handle = _handle;

                if (handle == 0)
                    ThrowHelper.ThrowInvalidOperationException();

                return InternalGetDependent(handle);
            }
            set
            {
                IntPtr handle = _handle;

                if (handle == 0)
                    ThrowHelper.ThrowInvalidOperationException();

                InternalSetDependent(handle, value);
            }
        }

        public readonly (object? Target, object? Dependent) TargetAndDependent
        {
            get
            {
                IntPtr handle = _handle;

                if (handle == 0)
                    ThrowHelper.ThrowInvalidOperationException();

                object? target = InternalGetTargetAndDependent(handle, out object? dependent);

                return (target, dependent);
            }
        }

        internal readonly object? UnsafeGetTarget() => InternalGetTarget(_handle);

        internal readonly object? UnsafeGetTargetAndDependent(out object? dependent) => InternalGetTargetAndDependent(_handle, out dependent);

        internal readonly void UnsafeSetTargetToNull() => InternalSetTargetToNull(_handle);

        internal readonly void UnsafeSetDependent(object? dependent) => InternalSetDependent(_handle, dependent);

        public void Dispose()
        {
            IntPtr handle = _handle;

            if (handle != 0)
            {
                _handle = 0;

                InternalFree(handle);
            }
        }

        private static IntPtr InternalAlloc(object? target, object? dependent) => RuntimeImports.RhHandleAllocDependent(target, dependent);

        private static object? InternalGetTarget(IntPtr dependentHandle) => RuntimeImports.RhHandleGet(dependentHandle);

        private static object? InternalGetDependent(IntPtr dependentHandle)
        {
            RuntimeImports.RhHandleGetDependent(dependentHandle, out object? dependent);

            return dependent;
        }

        private static object? InternalGetTargetAndDependent(IntPtr dependentHandle, out object? dependent)
            => RuntimeImports.RhHandleGetDependent(dependentHandle, out dependent);

        private static void InternalSetDependent(IntPtr dependentHandle, object? dependent)
            => RuntimeImports.RhHandleSetDependentSecondary(dependentHandle, dependent);

        private static void InternalSetTargetToNull(IntPtr dependentHandle) => RuntimeImports.RhHandleSet(dependentHandle, null);

        private static void InternalFree(IntPtr dependentHandle) => RuntimeImports.RhHandleFree(dependentHandle);
    }
}

namespace System.Runtime.ConstrainedExecution
{
    /// <summary>
    /// Ensures that all finalization code in derived classes is marked as critical.
    /// </summary>
    public abstract class CriticalFinalizerObject
    {
        protected CriticalFinalizerObject()
        {
        }

        ~CriticalFinalizerObject()
        {
        }
    }
}

namespace System.Runtime.ExceptionServices
{
    /// <summary>
    /// Provides helpers for exception handling, such as configuring and raising global unhandled exception handlers.
    /// </summary>
    public static class ExceptionHandling
    {
        private static Func<Exception, bool>? s_handler;

        internal static bool IsHandledByGlobalHandler(Exception ex)
        {
            return s_handler?.Invoke(ex) == true;
        }

        /// <summary>
        /// Sets a handler for unhandled exceptions.
        /// </summary>
        /// <param name="handler">A callback that will be invoked for unhandled exceptions. Return <see langword="true"/> if the exception was handled; otherwise <see langword="false"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler" /> is null.</exception>
        /// <exception cref="InvalidOperationException">A handler is already set.</exception>
        /// <remarks>
        /// The handler is called when an unhandled exception occurs.
        /// The handler should return <see langword="true" /> if the exception was handled, or <see langword="false" /> if it was not.
        /// If the handler returns false, the exception will continue to propagate as unhandled.
        ///
        /// The intent of this handler is to allow the user to handle unhandled exceptions
        /// gracefully when the runtime is being used in certain scenarios. Scenarios such
        /// as REPLs or game scripting that host plug-ins are not able to handle unhandled
        /// exceptions thrown by those plug-ins.
        /// </remarks>
        public static void SetUnhandledExceptionHandler(Func<Exception, bool> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            if (Interlocked.CompareExchange(ref s_handler, handler, null) != null)
            {
                throw new InvalidOperationException(SR.InvalidOperation_CannotRegisterSecondHandler);
            }
        }
    }

    public sealed class ExceptionDispatchInfo
    {
        private readonly Exception _exception;

        private readonly Exception.DispatchState _dispatchState;

        public Exception SourceException => _exception;

        private ExceptionDispatchInfo(Exception exception)
        {
            _exception = exception;
            _dispatchState = exception.CaptureDispatchState();
        }

        public static ExceptionDispatchInfo Capture(Exception source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new ExceptionDispatchInfo(source);
        }

        [DoesNotReturn]
        public void Throw()
        {
            _exception.RestoreDispatchState(_dispatchState);
            throw _exception;
        }

        [DoesNotReturn]
        public static void Throw(Exception source)
        {
            Capture(source).Throw();
        }

        public static Exception SetCurrentStackTrace(Exception source)
        {
            ArgumentNullException.ThrowIfNull(source);
            source.SetCurrentStackTrace();
            return source;
        }

        public static Exception SetRemoteStackTrace(Exception source, string stackTrace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(stackTrace);
            source.SetRemoteStackTrace(stackTrace);
            return source;
        }
    }
}

