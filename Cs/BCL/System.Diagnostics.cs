namespace System.Diagnostics
{
    public static class Debug
    {
        public static void Assert(bool condition) =>
            Assert(condition, string.Empty, string.Empty);
        public static void Assert(bool condition, string? message = null) =>
            Assert(condition, message, string.Empty);
        public static void Assert(bool condition, string? message, string? detailMessage)
        {
            if (!condition)
            {
                Fail(message, detailMessage);
            }
        }
        public static void Fail(string? message) =>
            Fail(message, string.Empty);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Fail(string? message, string? detailMessage) => throw new InvalidOperationException(message);
    }
}