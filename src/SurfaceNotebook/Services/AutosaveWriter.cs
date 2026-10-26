using System.IO;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

// One ordered worker keeps disk I/O, SQLite, and serialization off the pen/UI thread.
public sealed class AutosaveWriter : IDisposable
{
    private sealed record Work(Func<NotebookDatabase, object?> Run, TaskCompletionSource<object?> Completion, bool Mutation, bool RetryAllFailures);
    private readonly Channel<Work> queue = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private int pending;
    private string? error;
    private int stopped;
    private readonly TaskCompletionSource<LibrarySnapshot> initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<LibrarySnapshot> Initialized => initialized.Task;
    public int Pending => Volatile.Read(ref pending);
    public string? Error => Volatile.Read(ref error);
    public event Action? Changed;

    public AutosaveWriter(string directory) => worker = Task.Run(() => RunAsync(directory));

    public Task<T> ReadAsync<T>(Func<NotebookDatabase, T> action) => SubmitAsync<T>(db => action(db));
    public Task<T> MutateAsync<T>(Func<NotebookDatabase, T> action) => SubmitAsync<T>(db => action(db), mutation: true);
    public void Write(Action<NotebookDatabase> action)
    {
        var task = SubmitAsync<object?>(db => { action(db); return null; }, mutation: true, retryAllFailures: true);
        _ = task.ContinueWith(failed => { _ = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    private async Task<T> SubmitAsync<T>(Func<NotebookDatabase, object?> action, bool mutation = false, bool retryAllFailures = false)
    {
        await Initialized.ConfigureAwait(false);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref pending);
        if (!queue.Writer.TryWrite(new Work(action, completion, mutation, retryAllFailures)))
        {
            Interlocked.Decrement(ref pending);
            throw new ObjectDisposedException(nameof(AutosaveWriter));
        }
        Changed?.Invoke();
        return (T)(await completion.Task.ConfigureAwait(false))!;
    }
    public async Task<bool> FlushAsync(TimeSpan timeout)
    {
        try { await ReadAsync(_ => true).WaitAsync(timeout); return true; }
        catch (TimeoutException) { return false; }
    }
    public async Task<bool> FinishAsync(TimeSpan timeout)
    {
        try { await ReadAsync(db => { db.MarkClean(); return true; }).WaitAsync(timeout); return true; }
        catch (TimeoutException)
        {
            // Closing was canceled. A delayed clean marker must not hide a later crash.
            Write(db => db.MarkActive());
            return false;
        }
    }

    private async Task RunAsync(string directory)
    {
        try
        {
            using var database = new NotebookDatabase(directory);
            initialized.SetResult(database.Snapshot);
            await foreach (var work in queue.Reader.ReadAllAsync(cancellation.Token))
            {
                while (true)
                {
                    try
                    {
                        if (work.Mutation) database.MarkActive();
                        var result = work.Run(database);
                        Volatile.Write(ref error, null);
                        Interlocked.Decrement(ref pending);
                        work.Completion.SetResult(result);
                        Changed?.Invoke();
                        break;
                    }
                    catch (OperationCanceledException ex) when (!work.RetryAllFailures)
                    {
                        Volatile.Write(ref error, null); Interlocked.Decrement(ref pending);
                        work.Completion.SetCanceled(ex.CancellationToken); Changed?.Invoke(); break;
                    }
                    catch (Exception ex) when (work.RetryAllFailures || ex is IOException or UnauthorizedAccessException ||
                        ex is SqliteException sql && (!work.Mutation || sql.SqliteErrorCode is 5 or 6 or 10 or 13 or 14))
                    {
                        // Keep this operation (and everything after it) queued in order.
                        Volatile.Write(ref error, ex.Message);
                        Changed?.Invoke();
                        await Task.Delay(1000, cancellation.Token);
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref error, ex.Message);
                        Interlocked.Decrement(ref pending);
                        work.Completion.SetException(ex);
                        Changed?.Invoke();
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { initialized.TrySetException(ex); Volatile.Write(ref error, ex.Message); Changed?.Invoke(); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        queue.Writer.TryComplete();
        cancellation.Cancel();
        _ = worker.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        // The worker owns and closes the SQLite connection and the instance lock.
    }
    public async Task StopAsync()
    {
        queue.Writer.TryComplete();
        await worker.ConfigureAwait(false);
        if (Interlocked.Exchange(ref stopped, 1) == 0) cancellation.Dispose();
    }
}
