namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Represents an <see cref="Abstractions.Observation"/> together with the
/// <see cref="MemoryScope"/> it belongs to, as published to an <see cref="IObservationSink"/>.
/// </summary>
/// <param name="Scope">The partition the observation belongs to.</param>
/// <param name="Observation">The captured observation.</param>
public sealed record ObservationCaptured(MemoryScope Scope, Observation Observation);
