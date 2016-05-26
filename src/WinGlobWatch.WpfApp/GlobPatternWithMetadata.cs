using Minimatch;

namespace WinGlobWatch.WpfApp
{
    public class GlobPatternWithMetadata : IPattern
    {
        private readonly Minimatcher _matcher;

        public GlobPatternWithMetadata(string pattern, string metadata)
        {
            _matcher = new Minimatcher(pattern.TrimStart('/'));
            Metadata = metadata;
        }

        public string Metadata { get; }

        public bool IsMatch(string path)
        {
            if (path != "/")
            {
                return _matcher.IsMatch(path.TrimStart('/'));
            }

            return false;
        }
    }
}