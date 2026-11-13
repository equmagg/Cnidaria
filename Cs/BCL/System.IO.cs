using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace System.IO
{
    public class DirectoryNotFoundException : IOException
    {
        public DirectoryNotFoundException() : base(SR.Arg_DirectoryNotFoundException)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
        }

        public DirectoryNotFoundException(string? message) : base(message)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
        }

        public DirectoryNotFoundException(string? message, Exception? innerException) : base(message, innerException)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
        }
    }


    // This abstract base class represents a reader that can read a sequential
    // stream of characters.  This is not intended for reading bytes -
    // there are methods on the Stream class to read bytes.
    // A subclass must minimally implement the Peek() and Read() methods.
    //
    // This class is intended for character input, not bytes.
    // There are methods on the Stream class for reading bytes.
    public abstract class TextReader : MarshalByRefObject, IDisposable
    {
        // Create our own instance to avoid static field initialization order problems on Mono.
        public static readonly TextReader Null = new StreamReader.NullStreamReader();

        protected TextReader() { }

        public virtual void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        // Returns the next available character without actually reading it from
        // the input stream. The current position of the TextReader is not changed by
        // this operation. The returned value is -1 if no further characters are
        // available.
        //
        // This default method simply returns -1.
        //
        public virtual int Peek()
        {
            return -1;
        }

        // Reads the next character from the input stream. The returned value is
        // -1 if no further characters are available.
        //
        // This default method simply returns -1.
        //
        public virtual int Read()
        {
            return -1;
        }

        // Reads a block of characters. This method will read up to
        // count characters from this TextReader into the
        // buffer character array starting at position
        // index. Returns the actual number of characters read.
        //
        public virtual int Read(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException(SR.Argument_InvalidOffLen);
            }

            int n;
            for (n = 0; n < count; n++)
            {
                int ch = Read();
                if (ch == -1) break;
                buffer[index + n] = (char)ch;
            }

            return n;
        }

        // Reads a span of characters. This method will read up to
        // count characters from this TextReader into the
        // span of characters Returns the actual number of characters read.
        //
        public virtual int Read(Span<char> buffer)
        {
            char[] array = ArrayPool<char>.Shared.Rent(buffer.Length);

            try
            {
                int numRead = Read(array, 0, buffer.Length);
                if ((uint)numRead > (uint)buffer.Length)
                {
                    throw new IOException(SR.IO_InvalidReadLength);
                }
                new Span<char>(array, 0, numRead).CopyTo(buffer);
                return numRead;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        // Reads all characters from the current position to the end of the
        // TextReader, and returns them as one string.
        public virtual string ReadToEnd()
        {
            char[] chars = new char[4096];
            int len;
            StringBuilder sb = new StringBuilder(4096);
            while ((len = Read(chars, 0, chars.Length)) != 0)
            {
                sb.Append(chars, 0, len);
            }
            return sb.ToString();
        }

        // Blocking version of read.  Returns only when count
        // characters have been read or the end of the file was reached.
        //
        public virtual int ReadBlock(char[] buffer, int index, int count)
        {
            int i, n = 0;
            do
            {
                n += (i = Read(buffer, index + n, count - n));
            } while (i > 0 && n < count);
            return n;
        }

        // Blocking version of read for span of characters.  Returns only when count
        // characters have been read or the end of the file was reached.
        //
        public virtual int ReadBlock(Span<char> buffer)
        {
            char[] array = ArrayPool<char>.Shared.Rent(buffer.Length);

            try
            {
                int numRead = ReadBlock(array, 0, buffer.Length);
                if ((uint)numRead > (uint)buffer.Length)
                {
                    throw new IOException(SR.IO_InvalidReadLength);
                }
                new Span<char>(array, 0, numRead).CopyTo(buffer);
                return numRead;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        // Reads a line. A line is defined as a sequence of characters followed by
        // a carriage return ('\r'), a line feed ('\n'), or a carriage return
        // immediately followed by a line feed. The resulting string does not
        // contain the terminating carriage return and/or line feed. The returned
        // value is null if the end of the input stream has been reached.
        //
        public virtual string? ReadLine()
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int ch = Read();
                if (ch == -1) break;
                if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && Peek() == '\n')
                    {
                        Read();
                    }

                    return sb.ToString();
                }
                sb.Append((char)ch);
            }
            if (sb.Length > 0)
            {
                return sb.ToString();
            }

            return null;
        }

        #region Task based Async APIs
        #endregion

        public static TextReader Synchronized(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            return reader is SyncTextReader ? reader : new SyncTextReader(reader);
        }

        internal sealed class SyncTextReader : TextReader
        {
            internal readonly TextReader _in;

            internal SyncTextReader(TextReader t)
            {
                _in = t;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Close() => _in.Close();

            [MethodImpl(MethodImplOptions.Synchronized)]
            protected override void Dispose(bool disposing)
            {
                // Explicitly pick up a potentially methodimpl'ed Dispose
                if (disposing)
                    ((IDisposable)_in).Dispose();
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override int Peek() => _in.Peek();

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override int Read() => _in.Read();

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override int Read(char[] buffer, int index, int count) => _in.Read(buffer, index, count);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override int ReadBlock(char[] buffer, int index, int count) => _in.ReadBlock(buffer, index, count);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override string? ReadLine() => _in.ReadLine();

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override string ReadToEnd() => _in.ReadToEnd();

            //
            // On SyncTextReader all APIs should run synchronously, even the async ones.
            //
        }
    }

    public abstract class Stream : MarshalByRefObject, IDisposable
    {
        public static readonly Stream Null = new NullStream();



        public abstract bool CanRead { get; }
        public abstract bool CanWrite { get; }
        public abstract bool CanSeek { get; }
        public virtual bool CanTimeout => false;

        public abstract long Length { get; }
        public abstract long Position { get; set; }

        public virtual int ReadTimeout
        {
            get => throw new InvalidOperationException(SR.InvalidOperation_TimeoutsNotSupported);
            set => throw new InvalidOperationException(SR.InvalidOperation_TimeoutsNotSupported);
        }

        public virtual int WriteTimeout
        {
            get => throw new InvalidOperationException(SR.InvalidOperation_TimeoutsNotSupported);
            set => throw new InvalidOperationException(SR.InvalidOperation_TimeoutsNotSupported);
        }

        public virtual void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose() => Close();

        protected virtual void Dispose(bool disposing)
        {
        }

        public abstract int Read(byte[] buffer, int offset, int count);

        /// <summary>Provides a nop stream.</summary>
        private sealed class NullStream : Stream
        {
            internal NullStream() { }

            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }

            protected override void Dispose(bool disposing)
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => 0;
        }
    }

    public enum FileMode
    {
        CreateNew = 1,
        Create = 2,
        Open = 3,
        OpenOrCreate = 4,
        Truncate = 5,
        Append = 6,
    }

    [Flags]
    public enum FileAccess
    {
        Read = 1,
        Write = 2,
        ReadWrite = 3,
    }

    [Flags]
    public enum FileShare
    {
        None = 0,
        Read = 1,
        Write = 2,
        ReadWrite = 3,
        Delete = 4,
        Inheritable = 16,
    }

    public sealed class FileStreamOptions
    {
        public FileMode Mode { get; set; } = FileMode.Open;
        public FileAccess Access { get; set; } = FileAccess.Read;
        public FileShare Share { get; set; } = FileShare.Read;
        public int BufferSize { get; set; } = FileStream.DefaultBufferSize;
    }

    public class FileStream : Stream
    {
        internal const int DefaultBufferSize = 4096;

        public FileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize) => throw new PlatformNotSupportedException();

        public FileStream(string path, FileStreamOptions options) => throw new PlatformNotSupportedException();

        public override bool CanRead => throw new PlatformNotSupportedException();
        public override bool CanWrite => throw new PlatformNotSupportedException();
        public override bool CanSeek => throw new PlatformNotSupportedException();
        public override long Length => throw new PlatformNotSupportedException();
        public override long Position
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new PlatformNotSupportedException();
    }

    // This class implements a TextReader for reading characters to a Stream.
    // This is designed for character input in a particular Encoding,
    // whereas the Stream class is designed for byte input and output.
    public class StreamReader : TextReader
    {
        // StreamReader.Null is threadsafe.
        public static new readonly StreamReader Null = new NullStreamReader();

        // Using a 1K byte buffer and a 4K FileStream buffer works out pretty well
        // perf-wise.  On even a 40 MB text file, any perf loss by using a 4K
        // buffer is negated by the win of allocating a smaller byte[], which
        // saves construction time.  This does break adaptive buffering,
        // but this is slightly faster.
        private const int DefaultBufferSize = 1024;  // Byte buffer size
        private const int MinBufferSize = 128;

        private readonly Stream _stream;
        private Encoding _encoding = null!; // only null in NullStreamReader where this is never used
        private Decoder _decoder = null!; // only null in NullStreamReader where this is never used
        private readonly byte[] _byteBuffer = null!; // only null in NullStreamReader where this is never used
        private char[] _charBuffer = null!; // only null in NullStreamReader where this is never used
        private int _charPos;
        private int _charLen;
        // Record the number of valid bytes in the byteBuffer, for a few checks.
        private int _byteLen;
        // This is used only for preamble detection
        private int _bytePos;

        // This is the maximum number of chars we can get from one call to
        // ReadBuffer.  Used so ReadBuffer can tell when to copy data into
        // a user's char[] directly, instead of our internal char[].
        private int _maxCharsPerBuffer;

        /// <summary>True if the writer has been disposed; otherwise, false.</summary>
        private bool _disposed;

        // We will support looking for byte order marks in the stream and trying
        // to decide what the encoding might be from the byte order marks, IF they
        // exist.  But that's all we'll do.
        private bool _detectEncoding;

        // Whether we must still check for the encoding's given preamble at the
        // beginning of this file.
        private bool _checkPreamble;

        // Whether the stream is most likely not going to give us back as much
        // data as we want the next time we call it.  We must do the computation
        // before we do any byte order mark handling and save the result.  Note
        // that we need this to allow users to handle streams used for an
        // interactive protocol, where they block waiting for the remote end
        // to send a response, like logging in on a Unix machine.
        private bool _isBlocked;

        // The intent of this field is to leave open the underlying stream when
        // disposing of this StreamReader.  A name like _leaveOpen is better,
        // but this type is serializable, and this field's name was _closable.
        private readonly bool _closable;  // Whether to close the underlying stream.

        // We don't guarantee thread safety on StreamReader, but we should at
        // least prevent users from trying to read anything while an Async
        // read from the same thread is in progress. We track this with the
        // following fields.
        //
        // Generally we prefer to use the bool _asyncIOInProgress, but in
        // certain cases for async1 this would require introducing a wrapper
        // state machine, and in those cases we use the Task _asyncReadTask
        // instead.
        //
        private bool _asyncIOInProgress;

        [DoesNotReturn]
        private static void ThrowAsyncIOInProgress() =>
            throw new InvalidOperationException(SR.InvalidOperation_AsyncIOInProgress);

        private void CheckAsyncTaskInProgress()
        {
            if (_asyncIOInProgress)
            {
                ThrowAsyncIOInProgress();
            }
        }

        private ThrowOnReadsScope GuardAgainstOtherReads()
        {
            return new ThrowOnReadsScope(this);
        }

        private readonly struct ThrowOnReadsScope : IDisposable
        {
            private readonly StreamReader _reader;

            public ThrowOnReadsScope(StreamReader reader)
            {
                reader._asyncIOInProgress = true;
                _reader = reader;
            }

            public void Dispose() => _reader._asyncIOInProgress = false;
        }

        // StreamReader by default will ignore illegal UTF8 characters. We don't want to
        // throw here because we want to be able to read ill-formed data without choking.
        // The high level goal is to be tolerant of encoding errors when we read and very strict
        // when we write. Hence, default StreamWriter encoding will throw on error.

        private StreamReader()
        {
            _stream = Stream.Null;
            _closable = true;
        }

        public StreamReader(Stream stream)
            : this(stream, detectEncodingFromByteOrderMarks: true)
        {
        }

        public StreamReader(Stream stream, bool detectEncodingFromByteOrderMarks)
            : this(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks, DefaultBufferSize, leaveOpen: false)
        {
        }

        public StreamReader(Stream stream, Encoding? encoding)
            : this(stream, encoding, detectEncodingFromByteOrderMarks: true, DefaultBufferSize, leaveOpen: false)
        {
        }

        public StreamReader(Stream stream, Encoding? encoding, bool detectEncodingFromByteOrderMarks)
            : this(stream, encoding, detectEncodingFromByteOrderMarks, DefaultBufferSize, leaveOpen: false)
        {
        }

        // Creates a new StreamReader for the given stream.  The
        // character encoding is set by encoding and the buffer size,
        // in bytes, is set by bufferSize.
        //
        // Note that detectEncodingFromByteOrderMarks is a very
        // loose attempt at detecting the encoding by looking at the first
        // 3 bytes of the stream.  It will recognize UTF-8, little endian
        // unicode, and big endian unicode text, but that's it.  If neither
        // of those three match, it will use the Encoding you provided.
        //
        public StreamReader(Stream stream, Encoding? encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
            : this(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize, leaveOpen: false)
        {
        }

        public StreamReader(Stream stream, Encoding? encoding = null, bool detectEncodingFromByteOrderMarks = true, int bufferSize = -1, bool leaveOpen = false)
        {
            if (stream == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.stream);
            }

            if (!stream.CanRead)
            {
                throw new ArgumentException(SR.Argument_StreamNotReadable);
            }

            if (bufferSize == -1)
            {
                bufferSize = DefaultBufferSize;
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

            _stream = stream;
            _encoding = encoding ??= Encoding.UTF8;
            _decoder = encoding.GetDecoder();
            if (bufferSize < MinBufferSize)
            {
                bufferSize = MinBufferSize;
            }

            _byteBuffer = new byte[bufferSize];
            _maxCharsPerBuffer = encoding.GetMaxCharCount(bufferSize);
            _charBuffer = new char[_maxCharsPerBuffer];
            _detectEncoding = detectEncodingFromByteOrderMarks;

            // If the preamble length is larger than the byte buffer length,
            // we'll never match it and will enter an infinite loop. This
            // should never happen in practice, but just in case, we'll skip
            // the preamble check for absurdly long preambles.
            int preambleLength = encoding.Preamble.Length;
            _checkPreamble = preambleLength > 0 && preambleLength <= bufferSize;

            _closable = !leaveOpen;
        }

        public StreamReader(string path)
            : this(path, detectEncodingFromByteOrderMarks: true)
        {
        }

        public StreamReader(string path, bool detectEncodingFromByteOrderMarks)
            : this(path, Encoding.UTF8, detectEncodingFromByteOrderMarks, DefaultBufferSize)
        {
        }

        public StreamReader(string path, Encoding? encoding)
            : this(path, encoding, detectEncodingFromByteOrderMarks: true, DefaultBufferSize)
        {
        }

        public StreamReader(string path, Encoding? encoding, bool detectEncodingFromByteOrderMarks)
            : this(path, encoding, detectEncodingFromByteOrderMarks, DefaultBufferSize)
        {
        }

        public StreamReader(string path, Encoding? encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
            : this(ValidateArgsAndOpenPath(path, bufferSize), encoding, detectEncodingFromByteOrderMarks, bufferSize, leaveOpen: false)
        {
        }

        public StreamReader(string path, FileStreamOptions options)
            : this(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, options)
        {
        }

        public StreamReader(string path, Encoding? encoding, bool detectEncodingFromByteOrderMarks, FileStreamOptions options)
            : this(ValidateArgsAndOpenPath(path, options), encoding, detectEncodingFromByteOrderMarks, DefaultBufferSize)
        {
        }

        private static FileStream ValidateArgsAndOpenPath(string path, FileStreamOptions options)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            ArgumentNullException.ThrowIfNull(options);
            if ((options.Access & FileAccess.Read) == 0)
            {
                throw new ArgumentException(SR.Argument_StreamNotReadable, nameof(options));
            }

            return new FileStream(path, options);
        }

        private static FileStream ValidateArgsAndOpenPath(string path, int bufferSize)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);

            if (bufferSize != -1)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
            }

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileStream.DefaultBufferSize);
        }

        public override void Close()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            // Dispose of our resources if this StreamReader is closable.
            if (_closable)
            {
                try
                {
                    // Note that Stream.Close() can potentially throw here. So we need to
                    // ensure cleaning up internal resources, inside the finally block.
                    if (disposing)
                    {
                        _stream.Close();
                    }
                }
                finally
                {
                    _charPos = 0;
                    _charLen = 0;
                    base.Dispose(disposing);
                }
            }
        }

        public virtual Encoding CurrentEncoding => _encoding;

        public virtual Stream BaseStream => _stream;

        // DiscardBufferedData tells StreamReader to throw away its internal
        // buffer contents.  This is useful if the user needs to seek on the
        // underlying stream to a known location then wants the StreamReader
        // to start reading from this new point.  This method should be called
        // very sparingly, if ever, since it can lead to very poor performance.
        // However, it may be the only way of handling some scenarios where
        // users need to re-read the contents of a StreamReader a second time.
        public void DiscardBufferedData()
        {
            CheckAsyncTaskInProgress();

            _byteLen = 0;
            _charLen = 0;
            _charPos = 0;
            // in general we'd like to have an invariant that encoding isn't null. However,
            // for startup improvements for NullStreamReader, we want to delay load encoding.
            if (_encoding != null)
            {
                _decoder = _encoding.GetDecoder();
            }
            _isBlocked = false;
        }

        public bool EndOfStream
        {
            get
            {
                ThrowIfDisposed();
                CheckAsyncTaskInProgress();

                if (_charPos < _charLen)
                {
                    return false;
                }

                // This may block on pipes!
                int numRead = ReadBuffer();
                return numRead == 0;
            }
        }

        public override int Peek()
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            if (_charPos == _charLen)
            {
                if (ReadBuffer() == 0)
                {
                    return -1;
                }
            }
            return _charBuffer[_charPos];
        }

        public override int Read()
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            if (_charPos == _charLen)
            {
                if (ReadBuffer() == 0)
                {
                    return -1;
                }
            }
            int result = _charBuffer[_charPos];
            _charPos++;
            return result;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException(SR.Argument_InvalidOffLen);
            }

            return ReadSpan(new Span<char>(buffer, index, count));
        }

        public override int Read(Span<char> buffer) =>
            GetType() == typeof(StreamReader) ? ReadSpan(buffer) :
            base.Read(buffer); // Defer to Read(char[], ...) if a derived type may have previously overridden it

        private int ReadSpan(Span<char> buffer)
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            int charsRead = 0;
            // As a perf optimization, if we had exactly one buffer's worth of
            // data read in, let's try writing directly to the user's buffer.
            bool readToUserBuffer = false;
            int count = buffer.Length;
            while (count > 0)
            {
                int n = _charLen - _charPos;
                if (n == 0)
                {
                    n = ReadBuffer(buffer.Slice(charsRead), out readToUserBuffer);
                }
                if (n == 0)
                {
                    break;  // We're at EOF
                }
                if (n > count)
                {
                    n = count;
                }
                if (!readToUserBuffer)
                {
                    new Span<char>(_charBuffer, _charPos, n).CopyTo(buffer.Slice(charsRead));
                    _charPos += n;
                }

                charsRead += n;
                count -= n;
                // This function shouldn't block for an indefinite amount of time,
                // or reading from a network stream won't work right.  If we got
                // fewer bytes than we requested, then we want to break right here.
                if (_isBlocked)
                {
                    break;
                }
            }

            return charsRead;
        }

        public override string ReadToEnd()
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            // Call ReadBuffer, then pull data out of charBuffer.
            StringBuilder sb = new StringBuilder(_charLen - _charPos);
            do
            {
                sb.Append(_charBuffer, _charPos, _charLen - _charPos);
                _charPos = _charLen;  // Note we consumed these characters
                ReadBuffer();
            } while (_charLen > 0);
            return sb.ToString();
        }

        public override int ReadBlock(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException(SR.Argument_InvalidOffLen);
            }
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();

            return base.ReadBlock(buffer, index, count);
        }

        public override int ReadBlock(Span<char> buffer)
        {
            if (GetType() != typeof(StreamReader))
            {
                // Defer to Read(char[], ...) if a derived type may have previously overridden it.
                return base.ReadBlock(buffer);
            }

            int i, n = 0;
            do
            {
                i = ReadSpan(buffer.Slice(n));
                n += i;
            } while (i > 0 && n < buffer.Length);
            return n;
        }

        // Trims n bytes from the front of the buffer.
        private void CompressBuffer(int n)
        {
            byte[] byteBuffer = _byteBuffer;
            _ = byteBuffer.Length; // allow compiler to prove object is not null
            new ReadOnlySpan<byte>(byteBuffer, n, _byteLen - n).CopyTo(byteBuffer);
            _byteLen -= n;
        }

        private void DetectEncoding()
        {
            byte[] byteBuffer = _byteBuffer;
            _detectEncoding = false;
            bool changedEncoding = false;

            ushort firstTwoBytes = BinaryPrimitives.ReadUInt16LittleEndian(byteBuffer);
            if (firstTwoBytes == 0xFFFE)
            {
                // Big Endian Unicode
                _encoding = Encoding.BigEndianUnicode;
                CompressBuffer(2);
                changedEncoding = true;
            }
            else if (firstTwoBytes == 0xFEFF)
            {
                // Little Endian Unicode, or possibly little endian UTF32
                if (_byteLen < 4 || byteBuffer[2] != 0 || byteBuffer[3] != 0)
                {
                    _encoding = Encoding.Unicode;
                    CompressBuffer(2);
                    changedEncoding = true;
                }
                else
                {
                    _encoding = Encoding.UTF32;
                    CompressBuffer(4);
                    changedEncoding = true;
                }
            }
            else if (_byteLen >= 3 && firstTwoBytes == 0xBBEF && byteBuffer[2] == 0xBF)
            {
                // UTF-8
                _encoding = Encoding.UTF8;
                CompressBuffer(3);
                changedEncoding = true;
            }
            else if (_byteLen >= 4 && firstTwoBytes == 0 && byteBuffer[2] == 0xFE && byteBuffer[3] == 0xFF)
            {
                // Big Endian UTF32
                _encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                CompressBuffer(4);
                changedEncoding = true;
            }
            else if (_byteLen == 2)
            {
                _detectEncoding = true;
            }
            // Note: in the future, if we change this algorithm significantly,
            // we can support checking for the preamble of the given encoding.

            if (changedEncoding)
            {
                _decoder = _encoding.GetDecoder();
                int newMaxCharsPerBuffer = _encoding.GetMaxCharCount(byteBuffer.Length);
                if (newMaxCharsPerBuffer > _maxCharsPerBuffer)
                {
                    _charBuffer = new char[newMaxCharsPerBuffer];
                }
                _maxCharsPerBuffer = newMaxCharsPerBuffer;
            }
        }

        private bool IsPreamble()
        {
            if (!_checkPreamble)
            {
                return false;
            }

            ReadOnlySpan<byte> preamble = _encoding.Preamble;
            int len = Math.Min(_byteLen, preamble.Length);

            for (int i = _bytePos; i < len; i++)
            {
                if (_byteBuffer[i] != preamble[i])
                {
                    _bytePos = 0;
                    _checkPreamble = false;
                    return false;
                }
            }
            _bytePos = len;

            if (_bytePos == preamble.Length)
            {
                CompressBuffer(preamble.Length);
                _bytePos = 0;
                _checkPreamble = false;
                _detectEncoding = false;
            }

            return _checkPreamble;
        }

        internal virtual int ReadBuffer()
        {
            _charLen = 0;
            _charPos = 0;

            if (!_checkPreamble)
            {
                _byteLen = 0;
            }

            bool eofReached = false;

            do
            {
                if (_checkPreamble)
                {
                    int len = _stream.Read(_byteBuffer, _bytePos, _byteBuffer.Length - _bytePos);

                    if (len == 0)
                    {
                        eofReached = true;
                        break;
                    }

                    _byteLen += len;
                }
                else
                {
                    _byteLen = _stream.Read(_byteBuffer, 0, _byteBuffer.Length);

                    if (_byteLen == 0)
                    {
                        eofReached = true;
                        break;
                    }
                }

                // _isBlocked == whether we read fewer bytes than we asked for.
                // Note we must check it here because CompressBuffer or
                // DetectEncoding will change byteLen.
                _isBlocked = (_byteLen < _byteBuffer.Length);

                // Check for preamble before detect encoding. This is not to override the
                // user supplied Encoding for the one we implicitly detect. The user could
                // customize the encoding which we will loose, such as ThrowOnError on UTF8
                if (IsPreamble())
                {
                    continue;
                }

                // If we're supposed to detect the encoding and haven't done so yet,
                // do it.  Note this may need to be called more than once.
                if (_detectEncoding && _byteLen >= 2)
                {
                    DetectEncoding();
                }

                _charLen = _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, 0, flush: false);
            } while (_charLen == 0);

            if (eofReached)
            {
                // EOF has been reached - perform final flush.
                // We need to reset _bytePos and _byteLen just in case we hadn't
                // finished processing the preamble before we reached EOF.

                _charLen = _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, 0, flush: true);
                _bytePos = 0;
                _byteLen = 0;
            }

            return _charLen;
        }

        private int ReadBuffer(Span<char> userBuffer, out bool readToUserBuffer)
        {
            _charLen = 0;
            _charPos = 0;

            if (!_checkPreamble)
            {
                _byteLen = 0;
            }

            bool eofReached = false;
            int charsRead = 0;

            readToUserBuffer = userBuffer.Length >= _maxCharsPerBuffer;

            do
            {
                if (_checkPreamble)
                {
                    int len = _stream.Read(_byteBuffer, _bytePos, _byteBuffer.Length - _bytePos);

                    if (len == 0)
                    {
                        eofReached = true;
                        break;
                    }

                    _byteLen += len;
                }
                else
                {
                    _byteLen = _stream.Read(_byteBuffer, 0, _byteBuffer.Length);

                    if (_byteLen == 0)
                    {
                        eofReached = true;
                        break;
                    }
                }

                _isBlocked = (_byteLen < _byteBuffer.Length);

                if (IsPreamble())
                {
                    continue;
                }

                if (_detectEncoding && _byteLen >= 2)
                {
                    DetectEncoding();
                    readToUserBuffer = userBuffer.Length >= _maxCharsPerBuffer;
                }

                if (readToUserBuffer)
                {
                    charsRead = _decoder.GetChars(new ReadOnlySpan<byte>(_byteBuffer, 0, _byteLen), userBuffer, flush: false);
                }
                else
                {
                    charsRead = _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, 0, flush: false);
                    _charLen = charsRead;
                }
            } while (charsRead == 0);

            if (eofReached)
            {
                if (readToUserBuffer)
                {
                    charsRead = _decoder.GetChars(new ReadOnlySpan<byte>(_byteBuffer, 0, _byteLen), userBuffer, flush: true);
                }
                else
                {
                    charsRead = _decoder.GetChars(_byteBuffer, 0, _byteLen, _charBuffer, 0, flush: true);
                    _charLen = charsRead;
                }
                _bytePos = 0;
                _byteLen = 0;
            }

            _isBlocked &= charsRead < userBuffer.Length;

            return charsRead;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                ThrowObjectDisposedException();
            }

            void ThrowObjectDisposedException() => throw new ObjectDisposedException(GetType().Name, SR.ObjectDisposed_ReaderClosed);
        }

        // No data, class doesn't need to be serializable.
        // Note this class is threadsafe.
        internal sealed class NullStreamReader : StreamReader
        {
            public override Encoding CurrentEncoding => Encoding.Unicode;

            protected override void Dispose(bool disposing)
            {
                // Do nothing - this is essentially unclosable.
            }

            public override int Peek() => -1;

            public override int Read() => -1;

            public override int Read(char[] buffer, int index, int count) => 0;

            public override int Read(Span<char> buffer) => 0;

            public override int ReadBlock(char[] buffer, int index, int count) => 0;

            public override int ReadBlock(Span<char> buffer) => 0;

            public override string? ReadLine() => null;

            public override string ReadToEnd() => "";

            internal override int ReadBuffer() => 0;
        }
    }

    // This abstract base class represents a writer that can write a sequential
    // stream of characters. A subclass must minimally implement the
    // Write(char) method.
    //
    // This class is intended for character output, not bytes.
    // There are methods on the Stream class for writing bytes.
    public abstract class TextWriter : MarshalByRefObject, IDisposable
    {
        public static readonly TextWriter Null = new NullTextWriter();

        // We don't want to allocate on every TextWriter creation, so cache the char array.
        private static readonly char[] s_coreNewLine = Environment.NewLineConst.ToCharArray();

        // s_coreNewLine will be ['\r', '\n'] on Windows and ['\n'] on Unix. This is the opposite, lazily created/cached.
        private static char[]? s_otherCoreNewLine;

        /// <summary>
        /// This is the 'NewLine' property expressed as a char[].
        /// It is exposed to subclasses as a protected field for read-only
        /// purposes.  You should only modify it by using the 'NewLine' property.
        /// In particular you should never modify the elements of the array
        /// as they are shared among many instances of TextWriter.
        /// </summary>
        protected char[] CoreNewLine = s_coreNewLine;
        private string CoreNewLineStr = Environment.NewLineConst;

        // Can be null - if so, ask for the Thread's CurrentCulture every time.
        private readonly IFormatProvider? _internalFormatProvider;

        protected TextWriter()
        {
        }

        protected TextWriter(IFormatProvider? formatProvider)
        {
            _internalFormatProvider = formatProvider;
        }

        public virtual IFormatProvider FormatProvider
            => _internalFormatProvider ?? CultureInfo.CurrentCulture;

        public virtual void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Clears all buffers for this TextWriter and causes any buffered data to be
        // written to the underlying device. This default method is empty, but
        // descendant classes can override the method to provide the appropriate
        // functionality.
        public virtual void Flush()
        {
        }

        public abstract Encoding Encoding
        {
            get;
        }

        /// <summary>
        /// Returns the line terminator string used by this TextWriter. The default line
        /// terminator string is Environment.NewLine, which is platform specific.
        /// On Windows this is a carriage return followed by a line feed ("\r\n").
        /// On OSX and Linux this is a line feed ("\n").
        /// </summary>
        /// <remarks>
        /// The line terminator string is written to the text stream whenever one of the
        /// WriteLine methods are called. In order for text written by
        /// the TextWriter to be readable by a TextReader, only one of the following line
        /// terminator strings should be used: "\r", "\n", or "\r\n".
        /// </remarks>
        [AllowNull]
        public virtual string NewLine
        {
            get => CoreNewLineStr;
            set
            {
                value ??= Environment.NewLineConst;

                if (CoreNewLineStr != value)
                {
                    CoreNewLineStr = value;
                    CoreNewLine =
                        value == Environment.NewLineConst ? s_coreNewLine : // current OS default
                        Environment.NewLineConst == "\r\n" && value == "\n" ? (s_otherCoreNewLine ??= ['\n']) : // other OS default
                        Environment.NewLineConst == "\n" && value == "\r\n" ? (s_otherCoreNewLine ??= ['\r', '\n']) : // other OS default
                        value.ToCharArray(); // unknown
                }
            }
        }

        // Writes a character to the text stream. This default method is empty,
        // but descendant classes can override the method to provide the
        // appropriate functionality.
        //
        public virtual void Write(char value)
        {
        }

        /// <summary>
        /// Writes a rune to the text stream.
        /// </summary>
        /// <param name="value">The rune to write to the text stream.</param>
        public virtual unsafe void Write(Rune value)
        {
            // Convert value to span
            ReadOnlySpan<char> valueChars = value.AsSpan(stackalloc char[Rune.MaxUtf16CharsPerRune]);

            // Write span
            Write(valueChars[0]);
            if (valueChars.Length > 1)
            {
                Write(valueChars[1]);
            }
        }

        // Writes a character array to the text stream. This default method calls
        // Write(char) for each of the characters in the character array.
        // If the character array is null, nothing is written.
        //
        public virtual void Write(char[]? buffer)
        {
            if (buffer != null)
            {
                Write(buffer, 0, buffer.Length);
            }
        }

        // Writes a range of a character array to the text stream. This method will
        // write count characters of data into this TextWriter from the
        // buffer character array starting at position index.
        //
        public virtual void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException(SR.Argument_InvalidOffLen);
            }

            for (int i = 0; i < count; i++) Write(buffer[index + i]);
        }

        // Writes a span of characters to the text stream.
        //
        public virtual void Write(ReadOnlySpan<char> buffer)
        {
            char[] array = ArrayPool<char>.Shared.Rent(buffer.Length);

            try
            {
                buffer.CopyTo(new Span<char>(array));
                Write(array, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        // Writes the text representation of a boolean to the text stream. This
        // method outputs either bool.TrueString or bool.FalseString.
        //
        public virtual void Write(bool value)
        {
            Write(value ? "True" : "False");
        }

        // Writes the text representation of an integer to the text stream. The
        // text representation of the given value is produced by calling the
        // int.ToString() method.
        //
        public virtual void Write(int value)
        {
            Write(value.ToString(FormatProvider));
        }

        // Writes the text representation of an integer to the text stream. The
        // text representation of the given value is produced by calling the
        // uint.ToString() method.
        //
        [CLSCompliant(false)]
        public virtual void Write(uint value)
        {
            Write(value.ToString(FormatProvider));
        }

        // Writes the text representation of a long to the text stream. The
        // text representation of the given value is produced by calling the
        // long.ToString() method.
        //
        public virtual void Write(long value)
        {
            Write(value.ToString(FormatProvider));
        }

        // Writes the text representation of an unsigned long to the text
        // stream. The text representation of the given value is produced
        // by calling the ulong.ToString() method.
        //
        [CLSCompliant(false)]
        public virtual void Write(ulong value)
        {
            Write(value.ToString(FormatProvider));
        }

        // Writes the text representation of a float to the text stream. The
        // text representation of the given value is produced by calling the
        // float.ToString(float) method.
        //
        public virtual void Write(float value)
        {
            Write(value.ToString(FormatProvider));
        }

        // Writes the text representation of a double to the text stream. The
        // text representation of the given value is produced by calling the
        // double.ToString(double) method.
        //
        public virtual void Write(double value)
        {
            Write(value.ToString(FormatProvider));
        }

        public virtual void Write(decimal value)
        {
            Write(value.ToString(FormatProvider));
        }

        // Writes a string to the text stream. If the given string is null, nothing
        // is written to the text stream.
        //
        public virtual void Write(string? value)
        {
            if (value != null)
            {
                Write(value.ToCharArray());
            }
        }

        // Writes the text representation of an object to the text stream. If the
        // given object is null, nothing is written to the text stream.
        // Otherwise, the object's ToString method is called to produce the
        // string representation, and the resulting string is then written to the
        // output stream.
        //
        public virtual void Write(object? value)
        {
            if (value != null)
            {
                if (value is IFormattable f)
                {
                    Write(f.ToString(null, FormatProvider));
                }
                else
                    Write(value.ToString());
            }
        }

        /// <summary>
        /// Equivalent to Write(stringBuilder.ToString()) however it uses the
        /// StringBuilder.GetChunks() method to avoid creating the intermediate string
        /// </summary>
        /// <param name="value">The string (as a StringBuilder) to write to the stream</param>
        public virtual void Write(StringBuilder? value)
        {
            if (value != null)
            {
                foreach (ReadOnlyMemory<char> chunk in value.GetChunks())
                    Write(chunk.Span);
            }
        }

        // Writes out a formatted string.  Uses the same semantics as
        // string.Format.
        //
        public virtual void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            Write(string.Format(FormatProvider, format, arg0));
        }

        // Writes out a formatted string.  Uses the same semantics as
        // string.Format.
        //
        public virtual void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            Write(string.Format(FormatProvider, format, arg0, arg1));
        }

        // Writes out a formatted string.  Uses the same semantics as
        // string.Format.
        //
        public virtual void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        {
            Write(string.Format(FormatProvider, format, arg0, arg1, arg2));
        }

        // Writes out a formatted string.  Uses the same semantics as
        // string.Format.
        //
        public virtual void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        {
            Write(string.Format(FormatProvider, format, arg));
        }

        /// <summary>
        /// Writes a formatted string to the text stream, using the same semantics as <see cref="string.Format(string, ReadOnlySpan{object?})"/>.
        /// </summary>
        /// <param name="format">A composite format string.</param>
        /// <param name="arg">An object span that contains zero or more objects to format and write.</param>
        public virtual void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg)
        {
            Write(string.Format(FormatProvider, format, arg));
        }

        // Writes a line terminator to the text stream. The default line terminator
        // is Environment.NewLine, but this value can be changed by setting the NewLine property.
        //
        public virtual void WriteLine()
        {
            Write(CoreNewLine);
        }

        // Writes a character followed by a line terminator to the text stream.
        //
        public virtual void WriteLine(char value)
        {
            Write(value);
            WriteLine();
        }

        /// <summary>
        /// Writes a rune followed by a line terminator to the text stream.
        /// </summary>
        /// <param name="value">The rune to write to the text stream.</param>
        public virtual unsafe void WriteLine(Rune value)
        {
            // Convert value to span
            ReadOnlySpan<char> valueChars = value.AsSpan(stackalloc char[Rune.MaxUtf16CharsPerRune]);

            if (valueChars.Length > 1)
            {
                Write(valueChars[0]);
                WriteLine(valueChars[1]);
            }
            else
            {
                WriteLine(valueChars[0]);
            }
        }

        // Writes an array of characters followed by a line terminator to the text
        // stream.
        //
        public virtual void WriteLine(char[]? buffer)
        {
            Write(buffer);
            WriteLine();
        }

        // Writes a range of a character array followed by a line terminator to the
        // text stream.
        //
        public virtual void WriteLine(char[] buffer, int index, int count)
        {
            Write(buffer, index, count);
            WriteLine();
        }

        public virtual void WriteLine(ReadOnlySpan<char> buffer)
        {
            char[] array = ArrayPool<char>.Shared.Rent(buffer.Length);

            try
            {
                buffer.CopyTo(new Span<char>(array));
                WriteLine(array, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        // Writes the text representation of a boolean followed by a line
        // terminator to the text stream.
        //
        public virtual void WriteLine(bool value)
        {
            Write(value);
            WriteLine();
        }

        // Writes the text representation of an integer followed by a line
        // terminator to the text stream.
        //
        public virtual void WriteLine(int value)
        {
            Write(value);
            WriteLine();
        }

        // Writes the text representation of an unsigned integer followed by
        // a line terminator to the text stream.
        //
        [CLSCompliant(false)]
        public virtual void WriteLine(uint value)
        {
            Write(value);
            WriteLine();
        }

        // Writes the text representation of a long followed by a line terminator
        // to the text stream.
        //
        public virtual void WriteLine(long value)
        {
            Write(value);
            WriteLine();
        }

        // Writes the text representation of an unsigned long followed by
        // a line terminator to the text stream.
        //
        [CLSCompliant(false)]
        public virtual void WriteLine(ulong value)
        {
            Write(value);
            WriteLine();
        }

        // Writes the text representation of a float followed by a line terminator
        // to the text stream.
        //
        public virtual void WriteLine(float value)
        {
            Write(value);
            WriteLine();
        }

        // Writes the text representation of a double followed by a line terminator
        // to the text stream.
        //
        public virtual void WriteLine(double value)
        {
            Write(value);
            WriteLine();
        }

        public virtual void WriteLine(decimal value)
        {
            Write(value);
            WriteLine();
        }

        // Writes a string followed by a line terminator to the text stream.
        //
        public virtual void WriteLine(string? value)
        {
            if (value != null)
            {
                Write(value);
            }
            Write(CoreNewLineStr);
        }

        /// <summary>
        /// Equivalent to WriteLine(stringBuilder.ToString()) however it uses the
        /// StringBuilder.GetChunks() method to avoid creating the intermediate string
        /// </summary>
        public virtual void WriteLine(StringBuilder? value)
        {
            Write(value);
            WriteLine();
        }

        // Writes the text representation of an object followed by a line
        // terminator to the text stream.
        //
        public virtual void WriteLine(object? value)
        {
            if (value == null)
            {
                WriteLine();
            }
            else
            {
                // Call WriteLine(value.ToString), not Write(Object), WriteLine().
                // This makes calls to WriteLine(Object) atomic.
                if (value is IFormattable f)
                {
                    WriteLine(f.ToString(null, FormatProvider));
                }
                else
                {
                    WriteLine(value.ToString());
                }
            }
        }

        // Writes out a formatted string and a new line.  Uses the same
        // semantics as string.Format.
        //
        public virtual void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            WriteLine(string.Format(FormatProvider, format, arg0));
        }

        // Writes out a formatted string and a new line.  Uses the same
        // semantics as string.Format.
        //
        public virtual void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            WriteLine(string.Format(FormatProvider, format, arg0, arg1));
        }

        // Writes out a formatted string and a new line.  Uses the same
        // semantics as string.Format.
        //
        public virtual void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        {
            WriteLine(string.Format(FormatProvider, format, arg0, arg1, arg2));
        }

        // Writes out a formatted string and a new line.  Uses the same
        // semantics as string.Format.
        //
        public virtual void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        {
            WriteLine(string.Format(FormatProvider, format, arg));
        }

        /// <summary>
        /// Writes out a formatted string and a new line to the text stream, using the same semantics as <see cref="string.Format(string, ReadOnlySpan{object?})"/>.
        /// </summary>
        /// <param name="format">A composite format string.</param>
        /// <param name="arg">An object span that contains zero or more objects to format and write.</param>
        public virtual void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg)
        {
            WriteLine(string.Format(FormatProvider, format, arg));
        }

        #region Task based Async APIs
        #endregion

        private sealed class NullTextWriter : TextWriter
        {
            internal NullTextWriter() { }

            public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;
            public override Encoding Encoding => Encoding.Unicode;
            [AllowNull]
            public override string NewLine { get => base.NewLine; set { } }

            // To avoid all unnecessary overhead in the base, override all Flush/Write methods as pure nops.

            public override void Flush() { }

            public override void Write(char value) { }
            public override void Write(Rune value) { }
            public override void Write(char[]? buffer) { }
            public override void Write(char[] buffer, int index, int count) { }
            public override void Write(ReadOnlySpan<char> buffer) { }
            public override void Write(bool value) { }
            public override void Write(int value) { }
            public override void Write(uint value) { }
            public override void Write(long value) { }
            public override void Write(ulong value) { }
            public override void Write(float value) { }
            public override void Write(double value) { }
            public override void Write(decimal value) { }
            public override void Write(string? value) { }
            public override void Write(object? value) { }
            public override void Write(StringBuilder? value) { }
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0) { }
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1) { }
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2) { }
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg) { }
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg) { }

            public override void WriteLine() { }
            public override void WriteLine(char value) { }
            public override void WriteLine(Rune value) { }
            public override void WriteLine(char[]? buffer) { }
            public override void WriteLine(char[] buffer, int index, int count) { }
            public override void WriteLine(ReadOnlySpan<char> buffer) { }
            public override void WriteLine(bool value) { }
            public override void WriteLine(int value) { }
            public override void WriteLine(uint value) { }
            public override void WriteLine(long value) { }
            public override void WriteLine(ulong value) { }
            public override void WriteLine(float value) { }
            public override void WriteLine(double value) { }
            public override void WriteLine(decimal value) { }
            public override void WriteLine(string? value) { }
            public override void WriteLine(StringBuilder? value) { }
            public override void WriteLine(object? value) { }
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0) { }
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1) { }
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2) { }
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg) { }
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg) { }
        }

        public static TextWriter Synchronized(TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);

            return !RuntimeFeature.IsMultithreadingSupported || writer is SyncTextWriter
                    ? writer
                    : new SyncTextWriter(writer);
        }

        internal sealed class SyncTextWriter : TextWriter, IDisposable
        {
            private readonly TextWriter _out;

            internal SyncTextWriter(TextWriter t)
            {
                _out = t;
            }

            public override Encoding Encoding => _out.Encoding;

            public override IFormatProvider FormatProvider => _out.FormatProvider;

            [AllowNull]
            public override string NewLine
            {
                [MethodImpl(MethodImplOptions.Synchronized)]
                get => _out.NewLine;
                [MethodImpl(MethodImplOptions.Synchronized)]
                set => _out.NewLine = value;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Close() => _out.Close();

            [MethodImpl(MethodImplOptions.Synchronized)]
            protected override void Dispose(bool disposing)
            {
                // Explicitly pick up a potentially methodimpl'ed Dispose
                if (disposing)
                    ((IDisposable)_out).Dispose();
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Flush() => _out.Flush();

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(char value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(Rune value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(char[]? buffer) => _out.Write(buffer);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(char[] buffer, int index, int count) => _out.Write(buffer, index, count);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(ReadOnlySpan<char> buffer) => _out.Write(buffer);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(bool value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(int value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(uint value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(long value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(ulong value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(float value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(double value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(decimal value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(string? value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(StringBuilder? value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write(object? value) => _out.Write(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0) => _out.Write(format, arg0);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1) => _out.Write(format, arg0, arg1);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2) => _out.Write(format, arg0, arg1, arg2);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object?[] arg) => _out.Write(format, arg);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg) => _out.Write(format, arg);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine() => _out.WriteLine();

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(char value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(Rune value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(decimal value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(char[]? buffer) => _out.WriteLine(buffer);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(char[] buffer, int index, int count) => _out.WriteLine(buffer, index, count);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(ReadOnlySpan<char> buffer) => _out.WriteLine(buffer);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(bool value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(int value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(uint value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(long value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(ulong value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(float value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(double value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(string? value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(StringBuilder? value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine(object? value) => _out.WriteLine(value);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0) 
                => _out.WriteLine(format, arg0);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1) 
                => _out.WriteLine(format, arg0, arg1);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2) 
                => _out.WriteLine(format, arg0, arg1, arg2);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object?[] arg) => _out.WriteLine(format, arg);

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg)
                => _out.WriteLine(format, arg);

            //
            // On SyncTextWriter all APIs should run synchronously, even the async ones.
            //

        }
    }
}
