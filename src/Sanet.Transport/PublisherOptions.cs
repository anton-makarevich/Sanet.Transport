namespace Sanet.Transport;

/// <summary>
/// Marker base type for transport-specific publisher options.
/// Concrete options types live with their transport package and can carry
/// transport-specific settings. This base type remains transport-neutral,
/// so in-process and Rx option types can derive from it as well.
/// </summary>
public abstract record PublisherOptions;
