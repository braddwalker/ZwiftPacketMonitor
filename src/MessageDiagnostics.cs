using System.Collections.Generic;
using System.IO;

namespace ZwiftPacketMonitor
{
    public class MessageDiagnostics
    {
        private readonly Dictionary<string, int> _messageTypeCounters = new();
        private string _outputPath;
        private bool _initialized;

        public void OutputTo(string outputPath)
        {
            _outputPath = outputPath;
            if (!string.IsNullOrEmpty(_outputPath))
            {
                if (!Directory.Exists(_outputPath))
                {
                    Directory.CreateDirectory(_outputPath);
                }
                
                _initialized = true;
            }
        }

        public void StoreMessageType(
            uint messageType, 
            byte[] buffer, 
            Direction direction, 
            uint sequenceNummber,
            int maxNumberOfMessages = 10)
        {
            // If no output path is set we don't want to store anything
            if (!_initialized)
            {
                return;
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