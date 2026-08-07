using OpenKh.Tools.ModsManager.Infrastructure;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OpenKh.Tools.ModsManager.Avalonia.Infrastructure
{
    /// <summary>
    /// Avalonia-specific adapter for workflows that expose their current task to a view model.
    /// Execution and reentrancy semantics are delegated to the shared Core command primitive.
    /// </summary>
    public sealed class AvaloniaAsyncCommand : ICommand
    {
        private readonly AsyncCommand _command;
        private readonly Action<Task> _taskChanged;
        private bool _isEnabled = true;

        public AvaloniaAsyncCommand(
            Func<object, Task> execute,
            Action<Task> taskChanged = null,
            Func<Exception, Task> exceptionHandler = null)
        {
            ArgumentNullException.ThrowIfNull(execute);
            _taskChanged = taskChanged;
            _command = new AsyncCommand(parameter => ExecuteAndReportAsync(parameter, execute, exceptionHandler),
                _ => IsEnabled);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                    return;

                _isEnabled = value;
                _command.RaiseCanExecuteChanged();
            }
        }

        public bool IsExecuting => _command.IsExecuting;

        public event EventHandler CanExecuteChanged
        {
            add => _command.CanExecuteChanged += value;
            remove => _command.CanExecuteChanged -= value;
        }

        public bool CanExecute(object parameter) => _command.CanExecute(parameter);

        public void Execute(object parameter) => _command.Execute(parameter);

        public Task ExecuteAsync(object parameter = null) => _command.ExecuteAsync(parameter);

        public void RaiseCanExecuteChanged() => _command.RaiseCanExecuteChanged();

        private async Task ExecuteAndReportAsync(
            object parameter,
            Func<object, Task> execute,
            Func<Exception, Task> exceptionHandler)
        {
            Task task;
            try
            {
                task = execute(parameter);
                _taskChanged?.Invoke(task);
                await task;
            }
            catch (Exception exception)
            {
                if (exceptionHandler == null)
                    throw;

                await exceptionHandler(exception);
            }
        }
    }
}
