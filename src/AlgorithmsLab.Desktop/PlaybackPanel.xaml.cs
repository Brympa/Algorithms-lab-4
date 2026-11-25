using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Desktop;

public partial class PlaybackPanel : UserControl
{
    private readonly object pauseGate = new();
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource? finished;
    private StepJournal? journal;
    private int delay = 150;
    private bool paused;
    private int permits;
    private bool closed;
    private int page;
    private bool follow = true;
    private long comparisons;
    private long changes;

    public PlaybackPanel() => InitializeComponent();

    public int Delay { set => DelaySlider.Value = value; }
    public int CurrentAnimationMs { get; private set; }

    internal async Task RunAsync<T>(Func<Action<SortStep, Action>, CancellationToken, T> work, Action<T>? completed = null)
    {
        if (cancellation is not null)
            return;

        using var source = new CancellationTokenSource();
        cancellation = source;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        finished = completion;
        var token = source.Token;
        try
        {
            journal?.Dispose();
            journal = null;
            journal = new();
            follow = true;
            comparisons = changes = 0;
            permits = 0;
            paused = false;
            RefreshLog();
            Pause.IsEnabled = Stop.IsEnabled = true;
            ShowStatus("Сортировка выполняется");
            var result = await Task.Run(() =>
            {
                var frameClock = Stopwatch.StartNew();
                return work((step, draw) =>
                {
                    var done = step.Kind == StepKind.Complete;
                    var manual = false;
                    lock (pauseGate)
                    {
                        if (!done)
                        {
                            while (paused && permits == 0 && !token.IsCancellationRequested)
                                Monitor.Wait(pauseGate);

                            token.ThrowIfCancellationRequested();
                            manual = paused;
                            if (manual)
                                permits--;
                        }
                    }

                    journal.Append(step);
                    if (step.Kind == StepKind.Compare)
                        comparisons++;
                    if (step.Kind is StepKind.Swap or StepKind.Write)
                        changes++;

                    var interval = Volatile.Read(ref delay);
                    var animMs = manual
                        ? 280
                        : interval > 0
                            ? Math.Clamp((int)(interval * 0.82), 45, 420)
                            : 0;

                    if (manual || interval > 0 || frameClock.ElapsedMilliseconds >= 33 ||
                        step.Kind is StepKind.Start or StepKind.Complete)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (!closed)
                            {
                                CurrentAnimationMs = animMs;
                                draw();
                                RefreshLog();
                            }
                        }, DispatcherPriority.Background, done ? CancellationToken.None : token);
                        frameClock.Restart();
                    }

                    if (!manual && interval > 0 && step.Kind != StepKind.Complete)
                        token.WaitHandle.WaitOne(interval);
                }, token);
            }, token);

            if (!closed)
            {
                completed?.Invoke(result);
                ShowStatus("Сортировка завершена");
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (!closed)
                ShowStatus("Сортировка остановлена");
        }
        catch (Exception error)
        {
            if (!closed)
                ShowStatus(error.Message, error: true);
        }
        finally
        {
            cancellation = null;
            CurrentAnimationMs = 0;
            if (closed)
                journal?.Dispose();
            else
            {
                Pause.Content = "Пауза";
                Pause.IsEnabled = Single.IsEnabled = Stop.IsEnabled = false;
                RefreshLog();
            }
            finished = null;
            completion.SetResult();
        }
    }

    private void DelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Volatile.Write(ref delay, (int)e.NewValue);

    private void TogglePause(object sender, RoutedEventArgs e)
    {
        lock (pauseGate)
        {
            paused = !paused;
            permits = 0;
            Monitor.PulseAll(pauseGate);
        }
        Pause.Content = paused ? "Дальше" : "Пауза";
        Single.IsEnabled = paused;
        ShowStatus(paused ? "Пауза" : "Сортировка выполняется");
    }

    private void StepOnce(object sender, RoutedEventArgs e)
    {
        lock (pauseGate)
        {
            permits = 1;
            Monitor.PulseAll(pauseGate);
        }
    }

    private void StopRun(object sender, RoutedEventArgs e)
    {
        Pause.IsEnabled = Single.IsEnabled = Stop.IsEnabled = false;
        Cancel();
    }

    private void Cancel()
    {
        lock (pauseGate)
        {
            cancellation?.Cancel();
            Monitor.PulseAll(pauseGate);
        }
    }

    public Task CloseAsync()
    {
        closed = true;
        if (finished is { } completion)
        {
            Cancel();
            return completion.Task;
        }

        journal?.Dispose();
        return Task.CompletedTask;
    }

    public void ShowStatus(string message, bool error = false)
    {
        Status.Text = message;
        Status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Muted");
    }

    private void TurnPage(object sender, RoutedEventArgs e)
    {
        var delta = ((Button)sender).Tag as string == "1" ? 1 : -1;
        page += delta;
        follow = false;
        RefreshLog();
        follow = !NextPage.IsEnabled;
    }

    private void RefreshLog()
    {
        Comparisons.Text = comparisons.ToString("N0");
        Changes.Text = changes.ToString("N0");
        if (journal is null)
            return;

        var view = journal.Read(follow ? int.MaxValue : page);
        page = view.Page;
        LogList.ItemsSource = view.Lines;
        EmptyLog.Visibility = view.PageCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageLabel.Text = view.PageCount == 0 ? "0 / 0" : $"{page + 1:N0} / {view.PageCount:N0}";
        BackPage.IsEnabled = page > 0;
        NextPage.IsEnabled = page + 1 < view.PageCount;
    }
}
