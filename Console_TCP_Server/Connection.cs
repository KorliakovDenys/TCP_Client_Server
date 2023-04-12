using System.Buffers.Binary;
using System.Data.SqlClient;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace Console_TCP_Server{
    internal sealed class Connection : IDisposable{
        private const string ConnectionString =
            @"Data Source=188.239.119.71,1433;Initial Catalog=PC_Store;User ID=Guest;Password=qwe123;";

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly EndPoint? _remoteEndPoint;
        private Task _readingTask;
        private Task _writingTask;
        private readonly Action<Connection> _disposeCallback;
        private readonly Channel<string> _channel;
        private bool _disposed;

        internal delegate bool isRequestAllowedHandler(string remoteEndPoint);

        public event isRequestAllowedHandler IsRequestAllowed;

        public Connection(TcpClient client, Action<Connection> disposeCallback){
            _client = client;
            _stream = client.GetStream();
            _remoteEndPoint = client.Client.RemoteEndPoint;
            _disposeCallback = disposeCallback;
            _channel = Channel.CreateUnbounded<string>();
        }

        public void Dispose(){
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async Task SendMessageAsync(string message){
            var log = $"{DateTime.Now}| >> {_remoteEndPoint}: {message}";
            Console.WriteLine(log);

            await _channel.Writer.WriteAsync(message);
        }

        public async Task Run(){
            _readingTask ??= RunReadingLoop();
            _writingTask ??= RunWritingLoop();
        }

        private async Task RunReadingLoop(){
            await Task.Yield();
            try{
                var headerBuffer = new byte[sizeof(int)];
                while (IsRequestAllowed != null){
                    var bytesReceived = await _stream.ReadAsync(headerBuffer, 0, 4);
                    if (bytesReceived != sizeof(int))
                        break;
                    var length = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);
                    var buffer = new byte[length];
                    var count = 0;
                    while (count < length){
                        bytesReceived = await _stream.ReadAsync(buffer, count, buffer.Length - count);
                        count += bytesReceived;
                    }

                    var message = Encoding.ASCII.GetString(buffer, 0, length);
                    Console.WriteLine(
                        $"{DateTime.Now}|Received message from {_client.Client.RemoteEndPoint}: {message}");

                    var messageParts = message.Split(' ');

                    if (messageParts.Length != 2){
                        await SendMessageAsync($"{DateTime.Now}|Invalid request format");
                        continue;
                    }

                    var username = messageParts[0];
                    var password = messageParts[1];

                    if (!Authenticate(username, password)){
                        await SendMessageAsync($"{DateTime.Now}|{_client.Client.RemoteEndPoint} Authentication failed");
                        continue;
                    }

                    await SendMessageAsync("Authentication successful");
                    break;
                }

                while (IsRequestAllowed != null){
                    var isRequestAllowed =
                        IsRequestAllowed.Invoke(_client.Client.RemoteEndPoint.AddressFamily.ToString());

                    if (!isRequestAllowed){
                        await SendMessageAsync("You reached request limit");
                        break;
                    }

                    var bytesReceived = await _stream.ReadAsync(headerBuffer, 0, 4);
                    if (bytesReceived != sizeof(int))
                        break;
                    var length = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);
                    var buffer = new byte[length];
                    var count = 0;
                    while (count < length){
                        bytesReceived = await _stream.ReadAsync(buffer, count, buffer.Length - count);
                        count += bytesReceived;
                    }

                    var request = Encoding.UTF8.GetString(buffer);
                    var log = $"{DateTime.Now}| << {_remoteEndPoint}: {request}";
                    Console.WriteLine(log);

                    ////////////////////////////////////////////////////////////////////////////////////
                    var response = string.Empty;

                    try{
                        await using var connection = new SqlConnection(ConnectionString);
                        connection.Open();
                        var command = new SqlCommand("SELECT * FROM Product WHERE product LIKE @Name + '%'",
                            connection);
                        command.Parameters.AddWithValue("@Name", request);
                        var reader = await command.ExecuteReaderAsync();
                        while (reader.Read()){
                            var product = new Product(){
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Name = reader.GetString(reader.GetOrdinal("product")),
                                Price = Math.Round(reader.GetDecimal(reader.GetOrdinal("price")), 2)
                            };
                            response += "\n" + product;
                        }
                    }
                    catch (Exception exception){
                        response = exception.Message;
                    }
                    ////////////////////////////////////////////////////////////////////////////////////

                    await SendMessageAsync(response);
                }

                if (IsRequestAllowed == null) await SendMessageAsync("Server overloaded");

                Console.WriteLine($"{DateTime.Now}|Client {_remoteEndPoint} disconnected.");

                await Task.Delay(100);
                _stream.Close();
            }
            catch (IOException){
                Console.WriteLine($"{DateTime.Now}|Connection to {_remoteEndPoint} closed by server.");
            }
            catch (Exception ex){
                Console.WriteLine($"{DateTime.Now}|{ex.GetType().Name} : {ex.Message}");
            }

            if (!_disposed)
                _disposeCallback(this);
        }

        private bool Authenticate(string username, string password){
            return username == "admin" && password == "password";
        }

        private async Task RunWritingLoop(){
            var header = new byte[4];
            await foreach (string message in _channel.Reader.ReadAllAsync()){
                var buffer = Encoding.UTF8.GetBytes(message);
                BinaryPrimitives.WriteInt32LittleEndian(header, buffer.Length);
                await _stream.WriteAsync(header, 0, header.Length);
                await _stream.WriteAsync(buffer, 0, buffer.Length);
            }
        }


        void Dispose(bool disposing){
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
            _disposed = true;
            if (_client.Connected){
                _channel.Writer.Complete();
                _stream.Close();
                Task.WaitAll(_readingTask, _writingTask);
            }

            if (disposing){
                _client.Dispose();
            }
        }

        ~Connection() => Dispose(false);
    }
}