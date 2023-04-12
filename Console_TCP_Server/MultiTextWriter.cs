using System.Text;

namespace Console_TCP_Server {
    class MultiTextWriter : TextWriter {
        private readonly TextWriter[] _writers;

        public MultiTextWriter(params TextWriter[] writers) {
            _writers = writers;
        }

        public override Encoding Encoding => _writers[0].Encoding;

        public override void Write(char value) {
            foreach (var writer in _writers) {
                writer.Write(value);
            }
        }

        public override void Flush() {
            foreach (var writer in _writers) {
                writer.Flush();
            }
        }
    }
}