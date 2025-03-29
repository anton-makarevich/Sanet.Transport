using System.Net;
using System.Net.Sockets;
using System.Text;
using NSubstitute;
using Shouldly; 
using Xunit;
using Sanet.Transport.SignalR.Discovery;

namespace Sanet.Transport.SignalR.Tests.Discovery;

public class SignalRDiscoveryServiceTests
{
    private const int TestPort = 15001;
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.0.0.1");
    private readonly IPEndPoint _expectedMulticastEndpoint = new(MulticastAddress, TestPort);

    private readonly IUdpClientFactory _mockFactory;
    private readonly IUdpClientWrapper _mockSenderClient;
    private readonly IUdpClientWrapper _mockListenerClient;
    private readonly SignalRDiscoveryService _discoveryService;

    public SignalRDiscoveryServiceTests()
    {
        _mockFactory = Substitute.For<IUdpClientFactory>();
        _mockSenderClient = Substitute.For<IUdpClientWrapper>();
        _mockListenerClient = Substitute.For<IUdpClientWrapper>();

        _mockFactory.CreateSenderClient().Returns(_mockSenderClient);
        _mockFactory.CreateListenerClient(TestPort).Returns(_mockListenerClient);

        _discoveryService = new SignalRDiscoveryService(_mockFactory, TestPort);
    }

    [Fact]
    public void Constructor_WithDefaultFactory_DoesNotThrow()
    {
        // Arrange & Act
        var act = () => new SignalRDiscoveryService(TestPort);

        // Assert
        Should.NotThrow(act); 
    }
    
    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new SignalRDiscoveryService(null!, TestPort);

        // Assert
        var ex = Should.Throw<ArgumentNullException>(act); 
        ex.ParamName.ShouldBe("udpClientFactory");
    }

    [Fact]
    public async Task BroadcastPresence_SendsToMulticastEndpointPeriodically()
    {
        // Arrange
        const string hubUrl = "http://localhost:5000/hub";
        var expectedData = Encoding.UTF8.GetBytes(hubUrl);

        // Act
        _discoveryService.BroadcastPresence(hubUrl);
        await Task.Delay(100); // Allow time for the first broadcast task to start and send

        // Assert
        await _mockSenderClient.Received(1).SendAsync(Arg.Any<byte[]>(), expectedData.Length, _expectedMulticastEndpoint);

        // Act again: Wait for potential second broadcast
        await Task.Delay(5100); // Wait longer than the 5s interval

        // Assert again: Check if sent at least twice
        // Use NSubstitute.ReceivedExtensions.Quantity
        await _mockSenderClient.Received(2).SendAsync(Arg.Any<byte[]>(), expectedData.Length, _expectedMulticastEndpoint);
        
        // Cleanup
        _discoveryService.Stop();
        await Task.Delay(100); // Allow time for task to potentially stop
    }
    
    [Fact]
    public async Task BroadcastPresence_CalledTwice_OnlyStartsOneBroadcastLoop()
    {
        // Arrange
        var hubUrl = "http://localhost:5000/hub";
        
        // Act
        _discoveryService.BroadcastPresence(hubUrl);
        _discoveryService.BroadcastPresence(hubUrl); // Call again
        
        await Task.Delay(100); // Add delay to allow Task.Run to execute
        
        // Assert
        await Task.Delay(100);
        _mockFactory.Received(1).CreateSenderClient(); // Ensure sender client created only once
        
        // Cleanup needed to avoid test interference if broadcast task keeps running
        _discoveryService.Stop();
    }

    [Fact]
    public async Task StartListening_InitializesListenerAndJoinsMulticastGroup()
    {
        // Arrange
        // Simulate ReceiveAsync never returning to keep the loop conceptually running
        _mockListenerClient.ReceiveAsync().Returns(new TaskCompletionSource<UdpReceiveResult>().Task);

        // Act
        _discoveryService.StartListening();
        await Task.Delay(100); // Allow time for the listener task to start

        // Assert
        _mockFactory.Received(1).CreateListenerClient(TestPort);
        _mockListenerClient.Received(1).JoinMulticastGroup(MulticastAddress);
        _ = _mockListenerClient.Received(1).ReceiveAsync(); // Check that receive was called
    }

    [Fact]
    public async Task StartListening_ReceivesData_InvokesHostDiscovered()
    {
        // Arrange
        var hubUrl = "http://remotehost:1234/signalr";
        var receivedData = Encoding.UTF8.GetBytes(hubUrl);
        var udpResult = new UdpReceiveResult(receivedData, new IPEndPoint(IPAddress.Any, 0));
        string? discoveredUrl = null;
        _discoveryService.HostDiscovered += url => discoveredUrl = url;

        // Setup ReceiveAsync to return data once, then block indefinitely 
        var receiveTcs = new TaskCompletionSource<UdpReceiveResult>();
        _mockListenerClient.ReceiveAsync().Returns(Task.FromResult(udpResult), receiveTcs.Task);

        // Act
        _discoveryService.StartListening();
        await Task.Delay(100); // Allow ReceiveAsync to complete

        // Assert
        discoveredUrl.ShouldBe(hubUrl); // Use Shouldly
        _mockFactory.Received(1).CreateListenerClient(TestPort);
        _mockListenerClient.Received(1).JoinMulticastGroup(MulticastAddress);
        _ = _mockListenerClient.Received(2).ReceiveAsync(); // Called once for data, once for blocking
    }

    [Fact]
    public async Task StartListening_CalledTwice_OnlyStartsOneListenerLoop()
    {
        // Arrange
        _mockListenerClient.ReceiveAsync().Returns(new TaskCompletionSource<UdpReceiveResult>().Task);
        
        // Act
        _discoveryService.StartListening();
        await Task.Delay(50); // Small delay
        _discoveryService.StartListening(); // Call again
        await Task.Delay(50); 
        
        // Assert
        _mockFactory.Received(1).CreateListenerClient(TestPort); // Ensure listener client created only once
        _mockListenerClient.Received(1).JoinMulticastGroup(MulticastAddress);
    }

    [Fact]
    public async Task Stop_StopsBroadcastingAndListening_DisposesListener()
    {
        // Arrange
        var hubUrl = "http://localhost:5000/hub";
        var receiveTcs = new TaskCompletionSource<UdpReceiveResult>();
        _mockListenerClient.ReceiveAsync().Returns(receiveTcs.Task); // Block listener

        _discoveryService.BroadcastPresence(hubUrl);
        _discoveryService.StartListening();
        await Task.Delay(100); // Let loops start
        
        // Assert startup conditions (optional but good)
        await _mockSenderClient.Received(1).SendAsync(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<IPEndPoint>());
        _mockListenerClient.Received(1).JoinMulticastGroup(MulticastAddress);

        // Act
        _discoveryService.Stop();
        await Task.Delay(100); // Allow time for stop actions
        
        // Simulate ReceiveAsync throwing ObjectDisposedException because client was closed
        receiveTcs.SetException(new ObjectDisposedException("UdpClient"));
        await Task.Delay(100); // Allow task exception propagation

        // Assert stopping actions
        _mockListenerClient.Received(1).DropMulticastGroup(MulticastAddress);
        _mockListenerClient.Received(1).Close();
        _mockListenerClient.Received(1).Dispose();
        
        // Check broadcast stopped (won't send again after stop)
        var initialSendCount = _mockSenderClient.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SendAsync");
        await Task.Delay(5100); // Wait broadcast interval
        var finalSendCount = _mockSenderClient.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SendAsync");
        finalSendCount.ShouldBe(initialSendCount); // Use Shouldly
    }
    
    [Fact]
    public async Task Dispose_StopsAndDisposesResources()
    {
        // Arrange
        var receiveTcs = new TaskCompletionSource<UdpReceiveResult>();
        _mockListenerClient.ReceiveAsync().Returns(receiveTcs.Task); // Block listener

        _discoveryService.StartListening();
        await Task.Delay(100); // Let loop start
        
        // Act
        _discoveryService.Dispose();
        await Task.Delay(100); // Allow time for stop/dispose actions
        
        // Simulate ReceiveAsync throwing ObjectDisposedException because client was closed
        receiveTcs.SetException(new ObjectDisposedException("UdpClient"));
        await Task.Delay(100); // Allow task exception propagation

        // Assert
        _mockListenerClient.Received(1).DropMulticastGroup(MulticastAddress);
        _mockListenerClient.Received(1).Close();
        _mockListenerClient.Received(1).Dispose();
        
        // Attempting actions after dispose should throw
        Should.Throw<ObjectDisposedException>(() => _discoveryService.StartListening()); // Use Shouldly
        Should.Throw<ObjectDisposedException>(() => _discoveryService.BroadcastPresence("test")); // Use Shouldly
    }
}
