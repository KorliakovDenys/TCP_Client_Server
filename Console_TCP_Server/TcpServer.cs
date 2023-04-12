using System.Net.Sockets;
using System.Net;

namespace Console_TCP_Server {
    internal class TcpServer : IDisposable {
        private static readonly TimeSpan TimeWindow = TimeSpan.FromHours(1);

        private const int MAXCONNECTIONS = 1;

        private const int MAXREQUESTPERWINDOW = 3;

        private readonly TcpListener _listener;

        private readonly List<Connection> _clients = new();

        private readonly Dictionary<string, Queue<DateTime>> _requestPerClient = new();

        bool disposed;

        public TcpServer(int port) {
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public async Task ListenAsync() {
            try {
                _listener.Start();
                Console.WriteLine($"{DateTime.Now}|Server started on {_listener.LocalEndpoint}");

                while (true) {
                    var client = await _listener.AcceptTcpClientAsync();

                    Console.WriteLine($"{DateTime.Now}|Connection: {client.Client.RemoteEndPoint} > {client.Client.LocalEndPoint}");

                    lock (_clients) {
                        var connection = new Connection(client, c => {
                            lock (_clients) {
                                _clients.Remove(c);
                            }

                            c.Dispose();
                        });

                        if (_clients.Count < MAXCONNECTIONS) {
                            connection.IsRequestAllowed += ConnectionOnIsRequestAllowed;
                            _clients.Add(connection);
                        }
                        connection.Run();
                        
                    }
                }
            }
            catch (SocketException) {
                Console.WriteLine($"{DateTime.Now}|Server stopped.");
            }
        }

        public void Stop() {
            _listener.Stop();
        }

        private bool ConnectionOnIsRequestAllowed(string clientIpAddress) {
            if (!_requestPerClient.TryGetValue(clientIpAddress, out var requestQueue)) {
                requestQueue = new Queue<DateTime>();
                _requestPerClient[clientIpAddress] = requestQueue;
            }

            while (requestQueue.Count > 0 && DateTime.UtcNow - requestQueue.Peek() > TimeWindow) {
                requestQueue.Dequeue();
            }

            if (requestQueue.Count > MAXREQUESTPERWINDOW) {
                return false;
            }

            requestQueue.Enqueue(DateTime.UtcNow);

            return true;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing) {
            if (disposed)
                throw new ObjectDisposedException(typeof(TcpServer).FullName);
            disposed = true;
            _listener.Stop();
            if (!disposing) return;

            lock (_clients) {
                if (_clients.Count <= 0) return;

                Console.WriteLine($"{DateTime.Now}|Disconnecting clients...");
                foreach (Connection client in _clients) {
                    client.Dispose();
                }

                Console.WriteLine($"{DateTime.Now}|Clients disconnected.");
            }
        }

        ~TcpServer() => Dispose(false);
    }
}