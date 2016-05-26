namespace WinGlobWatch.WpfApp
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            BindableBase.DispatchAction = a => Dispatcher.Invoke(a);
            ViewModel = new MainWindowViewModel();
        }

        public MainWindowViewModel ViewModel
        {
            get { return DataContext as MainWindowViewModel; }
            set { DataContext = value; }
        }
    }
}
