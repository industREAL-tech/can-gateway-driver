using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW.Network
{
    /// <summary>
    /// One UDP socket used for both directions. The local port is ephemeral,
    /// which is what the gateway's reply-to-sender transport requires: it sends
    /// its uplink back to the source address and port of whatever last talked to
    /// it, so the socket that sends must also be the socket that receives.
    /// </summary>
    public class AsyncUDPClient : IDisposable
    {
        private readonly string serverAddress;
        private readonly int serverPort;
        private readonly IPEndPoint remoteEndPoint;
        private readonly object sync = new object();

        private UdpClient? udpClient;
        private CancellationTokenSource? cts;
        private Task? receiveTask;
        private bool disposed;

        public delegate void DataReceivedHandler(byte[] pData);
        public event DataReceivedHandler? NotifyListeners;

        /// <summary>Raised when the receive loop fails for a reason other than a
        /// normal stop, so the owner can log it instead of losing it to Console.</summary>
        public event Action<Exception>? OnReceiveError;

        public bool IsRunning
        {
            get { lock (sync) { return udpClient != null; } }
        }

        public AsyncUDPClient(string serverAddress, int serverPort)
        {
            this.serverAddress = serverAddress;
            this.serverPort = serverPort;
            // Resolved once: Parse threw on every send before, and it throws on
            // a host name rather than an address.
            this.remoteEndPoint = new IPEndPoint(IPAddress.Parse(serverAddress), serverPort);
        }

        protected virtual void OnDataReceivedInvoker(byte[] pParams)
        {
            NotifyListeners?.Invoke(pParams);
        }

        public void SendMessage(string message)
        {
            SendMessage(Encoding.UTF8.GetBytes(message));
        }

        public void SendMessage(byte[] message)
        {
            UdpClient? client;
            lock (sync) { client = udpClient; }
            if (client == null)
                throw new InvalidOperationException("Not started - call Start() first.");

            // SendAsync's Task was previously dropped on the floor, so a failed
            // send became an unobserved exception instead of an error anyone saw.
            _ = client.SendAsync(message, message.Length, remoteEndPoint)
                      .ContinueWith(t => OnReceiveError?.Invoke(t.Exception!.GetBaseException()),
                                    TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task ReceiveLoopAsync(UdpClient client, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await client.ReceiveAsync(token).ConfigureAwait(false);
                    OnDataReceivedInvoker(result.Buffer);
                }
                catch (OperationCanceledException)
                {
                    break; // normal stop
                }
                catch (ObjectDisposedException)
                {
                    break; // normal stop
                }
                catch (Exception ex)
                {
                    OnReceiveError?.Invoke(ex);
                }
            }
        }

        /// <summary>
        /// Creates the socket and starts receiving. Safe to call again after
        /// Stop(): the socket is created here rather than in the constructor, so
        /// a stopped client can be restarted. Previously Stop() closed the only
        /// socket the instance would ever have, making reconnection impossible.
        /// </summary>
        public void Start()
        {
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (udpClient != null)
                    throw new InvalidOperationException("Already started.");

                udpClient = new UdpClient(0); // ephemeral local port
                cts = new CancellationTokenSource();
                receiveTask = Task.Run(() => ReceiveLoopAsync(udpClient, cts.Token));
            }
        }

        public void Stop()
        {
            UdpClient? client;
            CancellationTokenSource? tokenSource;
            Task? task;

            lock (sync)
            {
                client = udpClient;
                tokenSource = cts;
                task = receiveTask;
                udpClient = null;
                cts = null;
                receiveTask = null;
            }

            if (client == null)
                return;

            try { tokenSource?.Cancel(); } catch (ObjectDisposedException) { }
            client.Close();
            try { task?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
            tokenSource?.Dispose();
            client.Dispose();
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
