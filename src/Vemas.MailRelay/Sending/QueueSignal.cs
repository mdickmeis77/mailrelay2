using System.Threading.Channels;

namespace Vemas.MailRelay.Sending;

/// <summary>Weckt den Versand-Worker sofort, wenn eine neue Mail in der Queue liegt.</summary>
public sealed class QueueSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify()
    {
        _channel.Writer.TryWrite(true);
    }

    /// <summary>Wartet auf ein Signal oder bis zum Timeout.</summary>
    /// <returns>true bei Signal, false bei Timeout.</returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
