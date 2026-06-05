using AgentMemoryOS.Abstractions;

namespace AgentMemoryOS.Pipeline;

/// <summary>
/// The default <see cref="IObservationSink"/>: a thin, contention-free writer over an
/// <see cref="ObservationChannel"/>. Publishing only enqueues, keeping the agent's hot path
/// free of embedding and persistence work.
/// </summary>
public sealed class ChannelObservationSink : IObservationSink
{
    private readonly ObservationChannel queue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelObservationSink"/> class.
    /// </summary>
    /// <param name="queue">The shared queue drained by the reconciler.</param>
    public ChannelObservationSink(ObservationChannel queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        this.queue = queue;
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(ObservationCaptured observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return this.queue.Writer.WriteAsync(observation, cancellationToken);
    }
}
