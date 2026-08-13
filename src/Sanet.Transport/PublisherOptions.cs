namespace Sanet.Transport;

/// <summary>
/// Marker base type for transport-specific publisher options.
/// Concrete options types live with their transport package and stay
/// transport-neutral (no hub/host details) so in-process and Rx
/// publishers can derive from them as well.
/// </summary>
public abstract record PublisherOptions;
