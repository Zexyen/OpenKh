using OpenKh.Tools.ModsManager.Infrastructure;
using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Xunit;

namespace OpenKh.Tests.ModsManager
{
    public class CorePrimitivesTests
    {
        [Fact]
        public void ObservableObjectRaisesOnlyForChangedValues()
        {
            var target = new TestObservable();
            PropertyChangedEventArgs raised = null;
            target.PropertyChanged += (_, args) => raised = args;

            target.Value = "value";
            Assert.Equal(nameof(TestObservable.Value), raised?.PropertyName);

            raised = null;
            target.Value = "value";
            Assert.Null(raised);
        }

        [Fact]
        public void RelayCommandSupportsExplicitCanExecuteInvalidation()
        {
            var enabled = false;
            var invalidations = 0;
            var command = new RelayCommand(() => { }, () => enabled);
            command.CanExecuteChanged += (_, _) => invalidations++;

            Assert.False(command.CanExecute(null));
            enabled = true;
            command.RaiseCanExecuteChanged();

            Assert.True(command.CanExecute(null));
            Assert.Equal(1, invalidations);
        }

        [Fact]
        public async Task AsyncCommandRejectsReentrancyAndRestoresState()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var executions = 0;
            var command = new AsyncCommand(async () =>
            {
                executions++;
                await completion.Task;
            });

            var first = command.ExecuteAsync();
            var second = command.ExecuteAsync();
            Assert.True(command.IsExecuting);
            Assert.False(command.CanExecute(null));
            Assert.Equal(1, executions);

            completion.SetResult();
            await Task.WhenAll(first, second);
            Assert.False(command.IsExecuting);
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public async Task AsyncCommandRestoresStateAfterFailure()
        {
            var command = new AsyncCommand(() => Task.FromException(new InvalidOperationException("failure")));

            await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync());

            Assert.False(command.IsExecuting);
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public void PlatformDtosHaveValueSemantics()
        {
            var first = new MessageDialogRequest("Message", "Title", MessageDialogKind.Warning, MessageDialogButtons.YesNo);
            var second = new MessageDialogRequest("Message", "Title", MessageDialogKind.Warning, MessageDialogButtons.YesNo);

            Assert.Equal(first, second);
        }

        private sealed class TestObservable : ObservableObject
        {
            private string _value;

            public string Value
            {
                get => _value;
                set => SetProperty(ref _value, value);
            }
        }
    }
}
