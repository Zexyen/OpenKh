using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OpenKh.Tools.ModsManager.Infrastructure
{
    public sealed class CommandExecutionFailedEventArgs : EventArgs
    {
        public CommandExecutionFailedEventArgs(Exception exception) => Exception = exception;

        public Exception Exception { get; }
    }

    public class AsyncCommand : ICommand
    {
        private readonly Func<object, CancellationToken, Task> _execute;
        private readonly Predicate<object> _canExecute;
        private readonly CancellationToken _cancellationToken;
        private int _isExecuting;

        public AsyncCommand(Func<Task> execute, Func<bool> canExecute = null)
            : this((_, _) => execute(), canExecute == null ? null : _ => canExecute())
        {
            ArgumentNullException.ThrowIfNull(execute);
        }

        public AsyncCommand(Func<object, Task> execute, Predicate<object> canExecute = null)
            : this((parameter, _) => execute(parameter), canExecute)
        {
        }

        public AsyncCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null,
            CancellationToken cancellationToken = default)
            : this((_, token) => execute(token), canExecute == null ? null : _ => canExecute(), cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(execute);
        }

        public AsyncCommand(Func<object, CancellationToken, Task> execute, Predicate<object> canExecute = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(execute);
            _execute = execute;
            _canExecute = canExecute;
            _cancellationToken = cancellationToken;
        }

        public event EventHandler CanExecuteChanged;
        public event EventHandler<CommandExecutionFailedEventArgs> ExecutionFailed;

        public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

        public bool CanExecute(object parameter) => !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object parameter)
        {
            try
            {
                await ExecuteAsync(parameter).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ExecutionFailed?.Invoke(this, new CommandExecutionFailedEventArgs(exception));
            }
        }

        public async Task ExecuteAsync(object parameter = null)
        {
            if (!(_canExecute?.Invoke(parameter) ?? true) || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
                return;

            RaiseCanExecuteChanged();
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                await _execute(parameter, _cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref _isExecuting, 0);
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
