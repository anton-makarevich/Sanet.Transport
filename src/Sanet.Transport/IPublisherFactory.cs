namespace Sanet.Transport;

/// <summary>
/// Creates transport publisher instances from transport-specific options
/// </summary>
public interface IPublisherFactory
{
    /// <summary>
    /// Creates a transport publisher for the given options
    /// </summary>
    /// <param name="options">Transport-specific publisher options</param>
    /// <param name="cancellationToken">Token that cancels publisher creation</param>
    /// <returns>The created publisher</returns>
    Task<ITransportPublisher> Create(PublisherOptions options, CancellationToken cancellationToken = default);
}
