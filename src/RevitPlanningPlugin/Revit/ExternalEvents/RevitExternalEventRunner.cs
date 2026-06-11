using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace RevitPlanningPlugin.Revit.ExternalEvents
{
    /// <summary>
    /// Выполняет действия modeless UI внутри допустимого Revit API-контекста.
    /// </summary>
    public sealed class RevitExternalEventRunner : IExternalEventHandler, IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly Queue<Action<UIApplication>> _actions = new();
        private readonly ExternalEvent _externalEvent;

        public RevitExternalEventRunner()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public Task RunAsync(Action<UIApplication> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return RunAsync<object?>(app =>
            {
                action(app);
                return null;
            });
        }

        public Task<T> RunAsync<T>(Func<UIApplication, T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_syncRoot)
            {
                _actions.Enqueue(app =>
                {
                    try
                    {
                        completion.SetResult(action(app));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                });
            }

            _externalEvent.Raise();
            return completion.Task;
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication>? action;
                lock (_syncRoot)
                {
                    if (_actions.Count == 0)
                        return;

                    action = _actions.Dequeue();
                }

                action(app);
            }
        }

        public string GetName() => "RevitPlanningPlugin modeless external event runner";

        public void Dispose()
        {
            _externalEvent.Dispose();
        }
    }
}
