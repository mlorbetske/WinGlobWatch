namespace WinGlobWatch
{
    public interface IPattern
    {
        bool IsMatch(string path);
    }
}