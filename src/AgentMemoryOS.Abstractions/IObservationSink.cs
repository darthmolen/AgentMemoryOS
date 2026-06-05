namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Receives observations published on the hot path so they can be reconciled into durable
/// memory asynchronously, off the request path.
/// </summary>
/// <remarks>
/// This is the in-process precursor to the swarm compactor: the default implementation
/// hands work to a background reconciler. A future swarm build can replace the transport
/// without changing the provider's capture logic.
/// </remarks>
public interface IObservationSink
{
    /// <summary>
    /// Publishes a captured observation for asynchronous reconciliation.
    /// </summary>
    /// <param name="observation">The captured observation together with its scope.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that completes once the observation has been accepted for processing.</returns>
    public ValueTask PublishAsync(ObservationCaptured observation, CancellationToken cancellationToken = default);
}
