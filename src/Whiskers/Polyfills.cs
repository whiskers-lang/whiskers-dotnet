namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace System
{
    internal readonly struct Index
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _value = fromEnd ? ~value : value;
        }

        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;

        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;

        public static Index Start => new Index(0);
        public static Index End => new Index(0, true);
        public static Index FromStart(int value) => new Index(value);
        public static Index FromEnd(int value) => new Index(value, true);

        public static implicit operator Index(int value) => new Index(value);
    }

    internal readonly struct Range
    {
        public Index Start { get; }
        public Index End { get; }

        public Range(Index start, Index end) { Start = start; End = end; }

        public static Range All => new Range(Index.Start, Index.End);
        public static Range StartAt(Index start) => new Range(start, Index.End);
        public static Range EndAt(Index end) => new Range(Index.Start, end);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);
            return (start, end - start);
        }
    }
}
