using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW.Socket
{
    public class AsyncUDPBroadcastClient : IDisposable
    {
        private readonly UdpClient udpClient;
        private readonly string serverAddress;
        private readonly int serverPort;
        private Task? receiveTask;
        private CancellationTokenSource cancellationTokenSource;

        public AsyncUDPBroadcastClient(string serverAddress, int serverPort)
        {
            this.serverAddress = serverAddress;
            this.serverPort = serverPort;
            this.udpClient = new UdpClient();
            this.cancellationTokenSource = new CancellationTokenSource();
        }

        public delegate void DataReceivedHandler(byte[] pData);
        public event DataReceivedHandler? NotifyListeners;

        protected virtual void OnDataReceivedInvoker(byte[] pParams)
        {
            NotifyListeners?.Invoke(pParams);
        }

        public void SendMessage(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(serverAddress), serverPort);
            udpClient.Send(data, data.Length, endPoint);
            Debug.WriteLine($"Sent message: {message}");
        }

        public void SendMessage(byte[] message)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(serverAddress), serverPort);
            udpClient.Send(message, message.Length, endPoint);
            Debug.WriteLine($"Sent message: {message}");
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, serverPort));

            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        UdpReceiveResult result = await udpClient.ReceiveAsync().WaitAsync(token);
                        OnDataReceivedInvoker(result.Buffer);
                        // Debug.WriteLine($"Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}");
                    }
                    catch (OperationCanceledException)
                    {
                        // Graceful cancellation
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error receiving data: {ex.Message}");
                    }
                }
            }
            finally
            {
                udpClient.Close();
                Debug.WriteLine("UDP client closed.");
            }
        }

        public void Start()
        {
            receiveTask = ReceiveLoopAsync(cancellationTokenSource.Token);
        }

        public async Task StopAsync()
        {
            cancellationTokenSource.Cancel();

            if (receiveTask != null)
            {
                try
                {
                    await receiveTask;
                }
                catch (OperationCanceledException)
                {
                    // Swallow cancellation
                }
            }

            Debug.WriteLine("Receiver task stopped.");
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            udpClient.Close();
            cancellationTokenSource.Dispose();
        }
    }
}
