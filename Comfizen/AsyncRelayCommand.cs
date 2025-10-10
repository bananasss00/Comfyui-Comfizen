using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Comfizen;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<object, Task> _execute;
    private readonly Predicate<object> _canExecute;

    public AsyncRelayCommand(Func<object, Task> execute, Predicate<object> canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object parameter)
    {
        return _canExecute == null || _canExecute(parameter);
    }

    public async void Execute(object parameter)
    {
        await _execute(parameter);
    }
}