using System.Net;
using System.Net.Sockets;
using System.Text;
using NSubstitute;
using Shouldly; 
using Xunit;
using Sanet.Transport.SignalR.Discovery;
using Sanet.Transport.SignalR.Network;

namespace Sanet.Transport.SignalR.Tests.Discovery;

public class MulticastDiscoveryServiceTests
{
    private const int TestPort = 15001;
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.0.0.1");
    private readonly IPEndPoint _expectedMulticastEndpoint = new(MulticastAddress, TestPort);

    private readonly IUdpClientFactory _mockFactory;
    private readonly IUdpClientWrapper _mockSenderClient;
    private readonly IUdpClientWrapper _mockListenerClient;
    private readonly MulticastDiscoveryService _multicastDiscoveryService;

    public MulticastDiscoveryServiceTests()
    {
        _mockFactory = Substitute.For<IUdpClientFactory>();
        _mockSenderClient = Substitute.For<IUdpClientWrapper>();
        _mockListenerClient = Substitute.For<IUdpClientWrapper>();

        _mockFactory.CreateSenderClient().Returns(_mockSenderClient);
        _mockFactory.CreateListenerClient(TestPort).Returns(_mockListenerClient);

        _multicastDiscoveryService = new MulticastDiscoveryService(_mockFactory, TestPort);
    }

    [Fact]
    public void Constructor_WithDefaultFactory_DoesNotThrow()
    {
        // Arrange & Act
        var act = () => new MulticastDiscoveryService(TestPort);

        // Assert
        Should.NotThrow(act); 
    }
    
    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new MulticastDiscoveryService(null!, TestPort);

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
        _multicastDiscoveryService.BroadcastPresence(hubUrl);
        await Task.Delay(100); // Allow time for the first broadcast task to start and send

        // Assert
        await _mockSenderClient.Received(1).SendAsync(Arg.Any<byte[]>(), expectedData.Length, _expectedMulticastEndpoint);

        // Act again: Wait for potential second broadcast
        await Task.Delay(5100); // Wait longer than the 5s interval

        // Assert again: Check if sent at least twice
        // Use NSubstitute.ReceivedExtensions.Quantity
        await _mockSenderClient.Received(2).SendAsync(Arg.Any<byte[]>(), expectedData.Length, _expectedMulticastEndpoint);
        
        // Cleanup
        _multicastDiscoveryService.StopBroadcasting();
        await Task.Delay(100); // Allow time for task to potentially stop
    }
    
    [Fact]
    public async Task BroadcastPresence_CalledTwice_OnlyStartsOneBroadcastLoop()
    {
        // Arrange
        var hubUrl = "http://localhost:5000/hub";
        
        // Act
        _multicastDiscoveryService.BroadcastPresence(hubUrl);
        _multicastDiscoveryService.BroadcastPresence(hubUrl); // Call again
        
        await Task.Delay(100); // Add delay to allow Task.Run to execute
        
        // Assert
        await Task.Delay(100);
        _mockFactory.Received(1).CreateSenderClient(); // Ensure sender client created only once
        
        // Cleanup needed to avoid test interference if broadcast task keeps running
        _multicastDiscoveryService.StopBroadcasting();
    }

    [Fact]
    public async Task StartListening_InitializesListenerAndJoinsMulticastGroup()
    {
        // Arrange
        // Simulate ReceiveAsync never returning to keep the loop conceptually running
        _mockListenerClient.ReceiveAsync().Returns(new TaskCompletionSource<UdpReceiveResult>().Task);

        // Act
        _multicastDiscoveryService.StartListening();
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
        _multicastDiscoveryService.HostDiscovered += url => discoveredUrl = url;

        // Setup ReceiveAsync to return data once, then block indefinitely 
        var receiveTcs = new TaskCompletionSource<UdpReceiveResult>();
        _mockListenerClient.ReceiveAsync().Returns(Task.FromResult(udpResult), receiveTcs.Task);

        // Act
        _multicastDiscoveryService.StartListening();
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
        _multicastDiscoveryService.StartListening();
        await Task.Delay(50); // Small delay
        _multicastDiscoveryService.StartListening(); // Call again
        await Task.Delay(50); 
        
        // Assert
        _mockFactory.Received(1).CreateListenerClient(TestPort); // Ensure listener client created only once
        _mockListenerClient.Received(1).JoinMulticastGroup(MulticastAddress);
    }
    
    [Fact]
    public async Task Dispose_StopsAndDisposesResources()
    {
        // Arrange
        var receiveTcs = new TaskCompletionSource<UdpReceiveResult>();
        _mockListenerClient.ReceiveAsync().Returns(receiveTcs.Task); // Block listener

        _multicastDiscoveryService.StartListening();
        await Task.Delay(100); // Let loop start
        
        // Act
        _multicastDiscoveryService.Dispose();
        await Task.Delay(100); // Allow time for stop/dispose actions
        
        // Simulate ReceiveAsync throwing ObjectDisposedException because client was closed
        receiveTcs.SetException(new ObjectDisposedException("UdpClient"));
        await Task.Delay(100); // Allow task exception propagation

        // Assert
        _mockListenerClient.Received(1).DropMulticastGroup(MulticastAddress);
        _mockListenerClient.Received(1).Close();
        _mockListenerClient.Received(1).Dispose();
        
        // Attempting actions after dispose should throw
        Should.Throw<ObjectDisposedException>(() => _multicastDiscoveryService.StartListening()); // Use Shouldly
        Should.Throw<ObjectDisposedException>(() => _multicastDiscoveryService.BroadcastPresence("test")); // Use Shouldly
    }

    [Fact]
    public async Task TestListeningLoop_ContinuesAfterReceiveException()
    {
        // Arrange
        var generalException = new Exception("Simulated receive error");
        var receiveTcs = new TaskCompletionSource<UdpReceiveResult>(); // To block after exception
        
        // Setup ReceiveAsync: Throw exception once, then block indefinitely
        _mockListenerClient.ReceiveAsync()
            .Returns(
                Task.FromException<UdpReceiveResult>(generalException),
                receiveTcs.Task 
            );

        // Act
        _multicastDiscoveryService.StartListening();
        // Wait long enough for the exception path (includes 1s delay) and the next call
        await Task.Delay(1500); 

        // Assert
        // Verify ReceiveAsync was called twice: once throwing, once blocking
        _ = _mockListenerClient.Received(2).ReceiveAsync(); 
        // We can't easily assert Console.Error output here, 
        // but verifying the loop continues implies the catch block was executed.
    }

    [Fact]
    public async Task TestBroadcastLoop_ContinuesAfterSendException()
    {
        // Arrange
        const string hubUrl = "http://testurl";
        var expectedData = Encoding.UTF8.GetBytes(hubUrl);
        var generalException = new Exception("Simulated send error");
        var sendCounter = 0;
        
        _mockSenderClient.SendAsync(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<IPEndPoint>())
            .Returns(async _ => 
            { 
                sendCounter++;
                if (sendCounter == 1)
                {
                    throw generalException; 
                }
                await Task.CompletedTask; // Simulate successful send otherwise
            });

        // Act
        _multicastDiscoveryService.BroadcastPresence(hubUrl);
        // Wait for first send (throws), delay in catch (5s), next send attempt
        await Task.Delay(5500); 

        // Assert
        // Verify SendAsync was called at least twice: once throwing, once successful
        await _mockSenderClient.Received(2).SendAsync(Arg.Any<byte[]>(), expectedData.Length, _expectedMulticastEndpoint);
    }
}
