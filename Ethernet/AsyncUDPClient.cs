using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW.Network
{
    public class AsyncUDPClient : IDisposable
    {
        private readonly UdpClient udpClient;
        private readonly string serverAddress;
        private readonly int serverPort;
        private CancellationTokenSource? cts;
        private bool disposed = false;

        public delegate void DataReceivedHandler(byte[] pData);
        public event DataReceivedHandler? NotifyListeners;

        public AsyncUDPClient(string serverAddress, int serverPort)
        {
            this.serverAddress = serverAddress;
            this.serverPort = serverPort;
                udpClient = new UdpClient(0); // Bind to any available local port
        }

        protected virtual void OnDataReceivedInvoker(byte[] pParams)
        {
            NotifyListeners?.Invoke(pParams);
        }

        public void SendMessage(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            SendMessage(data);
        }

        public void SendMessage(byte[] message)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(serverAddress), serverPort);
            udpClient.SendAsync(message, message.Length, endPoint);
        }

        public async Task StartReceivingAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    OnDataReceivedInvoker(result.Buffer); /*FASZ*/
                }
                catch (ObjectDisposedException)
                {
                    break; // Expected when stopping
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving data: {ex.Message}");
                }
            }
        }

        public void Start()
        {
            if (cts != null)
                throw new InvalidOperationException("Already started.");

            cts = new CancellationTokenSource();
            _ = Task.Run(() => StartReceivingAsync(cts.Token));
        }

        public void Stop()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = null;
            }

            udpClient.Close();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                Stop();
            }

            disposed = true;
        }

        ~AsyncUDPClient()
        {
            Dispose(false);
        }
    }
}
