using Xunit;

namespace Sanet.Transport.SignalR.Tests.Infrastructure;

/// <summary>
/// Tests that exercise the static <c>TransportHub.MessageReceived</c> event must run sequentially
/// with each other to avoid cross-test races.
/// </summary>
[CollectionDefinition("TransportHub", DisableParallelization = true)]
public class TransportHubTestCollection;
