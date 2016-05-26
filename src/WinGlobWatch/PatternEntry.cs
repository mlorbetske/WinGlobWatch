namespace WinGlobWatch
{
    public class PatternEntry<TPattern>
        where TPattern : class, IPattern
    {
        public PatternEntry(EntryKind kind, TPattern pattern)
        {
            Kind = kind;
            Pattern = pattern;
        }

        public EntryKind Kind { get; }

        public TPattern Pattern { get; }
    }
}