using System;
using System.Windows.Input;

namespace WinGlobWatch.WpfApp
{
    public class ActionCommand : ICommand
    {
        private readonly Func<bool> _canExecute;
        private readonly Action _execute;
        private bool _canExecuteValue;

        public ActionCommand(Action execute, Func<bool> canExecute = null)
        {
            canExecute = canExecute ?? (() => true);
            _canExecuteValue = canExecute();
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            bool oldCanExecute = _canExecuteValue;
            _canExecuteValue = _canExecute();

            if (oldCanExecute != _canExecuteValue)
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            return _canExecuteValue;
        }

        public event EventHandler CanExecuteChanged;

        public void Execute(object parameter)
        {
            _execute();
        }
    }
}