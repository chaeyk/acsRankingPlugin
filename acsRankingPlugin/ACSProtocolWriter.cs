using System.Collections.Generic;
using System.IO;
using System.Text;

namespace acsRankingPlugin
{
    class ACSProtocolWriter
    {
        private BinaryWriter _binaryWriter;

        public byte[] Buffer { get; }
        public long Length { get { return _binaryWriter.BaseStream.Length; } }

        public ACSProtocolWriter(int bufferSize = 255)
        {
            Buffer = new byte[bufferSize];
            _binaryWriter = new BinaryWriter(new MemoryStream(Buffer));
        }

        public void Write(byte value)
        {
            _binaryWriter.Write(value);
        }

        public void Write(short value)
        {
            _binaryWriter.Write(value);
        }

        public void Write(ushort value)
        {
            _binaryWriter.Write(value);
        }

        public void Write(int value)
        {
            _binaryWriter.Write(value);
        }

        public void Write(uint value)
        {
            _binaryWriter.Write(value);
        }

        public void WriteStringW(string message)
        {
            _binaryWriter.Write((byte)(message.Length));
            _binaryWriter.Write(Encoding.UTF32.GetBytes(message));
        }

    }
}
