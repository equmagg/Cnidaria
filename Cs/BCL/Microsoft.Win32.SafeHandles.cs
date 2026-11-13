using System;
using System.Runtime.InteropServices;

namespace Microsoft.Win32.SafeHandles
{
    // Class of safe handle which uses 0 or -1 as an invalid handle.
    public abstract class SafeHandleZeroOrMinusOneIsInvalid : SafeHandle
    {
        protected SafeHandleZeroOrMinusOneIsInvalid(bool ownsHandle) : base(IntPtr.Zero, ownsHandle)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
    }

    public sealed class SafeFileHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private string? _path;
        private int _cachedFileType = -1;
        // not using bool? as it's not thread safe
        private NullableBool _canSeek /* = NullableBool.Undefined */;

        public SafeFileHandle() : base(true)
        {
        }

        public SafeFileHandle(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle() => throw new PlatformNotSupportedException();
    }

    public sealed partial class SafeWaitHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Creates a <see cref="T:Microsoft.Win32.SafeHandles.SafeWaitHandle" />.
        /// </summary>
        public SafeWaitHandle() : base(true)
        {
        }

        /// <summary>
        /// Creates a <see cref="T:Microsoft.Win32.SafeHandles.SafeWaitHandle" /> around a wait handle.
        /// </summary>
        /// <param name="existingHandle">Handle to wrap</param>
        /// <param name="ownsHandle">Whether to control the handle lifetime</param>
        public SafeWaitHandle(IntPtr existingHandle, bool ownsHandle) : base(ownsHandle)
        {
            SetHandle(existingHandle);
        }

        protected override bool ReleaseHandle()
        {
            System.Threading.WaitSubsystem.DeleteHandle(handle);
            return true;
        }
    }


}
