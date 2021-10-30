using System.Collections.Generic;
using System.IO;

namespace ZwiftPacketMonitor
{
    public class MessageDiagnostics
    {
        private readonly Dictionary<string, int> _messageTypeCounters = new();
        private string _outputPath;

        public void OutputTo(string outputPath)
        {
            _outputPath = outputPath;
        }

        public void StoreMessageType(
            uint messageType, 
            byte[] buffer, 
            Direction direction, 
            uint sequenceNummber,
            int maxNumberOfMessages = 10)
        {
            if (!Directory.Exists(_outputPath))
            {
                Directory.CreateDirectory(_outputPath);
            }

            var type = $"{direction.ToString().ToLower()}-{messageType}";

            if (!_messageTypeCounters.ContainsKey(type))
            {
                _messageTypeCounters.Add(type, 0);
            }

            if (_messageTypeCounters[type] < maxNumberOfMessages)
            {
                File.WriteAllBytes(
                    $"{_outputPath}\\{sequenceNummber:000000}-{direction.ToString().ToLower()}-{messageType:000}.bin",
                    buffer);

                _messageTypeCounters[type]++;
            }
        }
    }
}