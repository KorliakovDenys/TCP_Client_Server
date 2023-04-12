using System.Text;

namespace Console_TCP_Server {
    abstract class Program {
        public static async Task Main(string[] args) {
            await using var logWriter = new StreamWriter("../../../logs.txt", true);

            var consoleWriter = Console.Out;

            Console.SetOut(new MultiTextWriter(consoleWriter, logWriter));


            const int port = 13400;
            Console.WriteLine($"{DateTime.Now}|Starting server....");
            using (var server = new TcpServer(port)) {
                var serverTask = server.ListenAsync();
                while (true) {
                    var input = Console.ReadLine();
                    if (input != "stop") continue;
                    
                    Console.WriteLine($"{DateTime.Now}|Server is shutting down...");
                    server.Stop();
                    break;
                }

                await serverTask;
            }

            Console.WriteLine($"{DateTime.Now}|Press any key to exit...");
            Console.ReadKey(true);

            await Console.Out.FlushAsync();
            await logWriter.FlushAsync();
        }
    }
}