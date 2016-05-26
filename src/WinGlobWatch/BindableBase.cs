using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinGlobWatch
{
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public static Action<Action> DispatchAction { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            RaisePropertyChanged(propertyName);
        }

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool Set<T>(ref T val, T value, IEqualityComparer<T> comparer = null, [CallerMemberName] string propertyName = "")
        {
            IEqualityComparer<T> realComparer = comparer ?? EqualityComparer<T>.Default;

            if (!realComparer.Equals(val, value))
            {
                val = value;

                Action<Action> dispatchAction = DispatchAction ?? (a => a());
                dispatchAction(() => RaisePropertyChanged(propertyName));
                return true;
            }

            return false;
        }
    }
}