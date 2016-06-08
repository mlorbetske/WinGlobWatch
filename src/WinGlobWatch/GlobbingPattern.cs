using Minimatch;

namespace WinGlobWatch
{
    public class GlobbingPattern : IPattern
    {
        private readonly Minimatcher _matcher;

        public GlobbingPattern(string pattern)
        {
            _matcher = new Minimatcher(pattern);
        }

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
