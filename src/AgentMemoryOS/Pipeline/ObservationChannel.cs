using System.Threading.Channels;
using AgentMemoryOS.Abstractions;

namespace AgentMemoryOS.Pipeline;

/// <summary>
/// The in-process channel that carries captured observations from the hot path to the
/// background <see cref="MemoryReconciler"/>. Register it as a singleton so the sink and the
/// reconciler share one channel. This is the in-process precursor to the swarm compactor;
/// swapping it for a durable transport later leaves the provider's capture path unchanged.
/// </summary>
public sealed class ObservationChannel
{
    private readonly Channel<ObservationCaptured> channel = Channel.CreateUnbounded<ObservationCaptured>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// Gets the writer the observation sink uses to enqueue captured observations.
    /// </summary>
    public ChannelWriter<ObservationCaptured> Writer => this.channel.Writer;

    /// <summary>
    /// Gets the reader the background reconciler drains.
    /// </summary>
    public ChannelReader<ObservationCaptured> Reader => this.channel.Reader;
}
