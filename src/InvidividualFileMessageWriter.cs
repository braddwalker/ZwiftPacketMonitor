using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZwiftPacketMonitor
{
    /// <summary>
    /// Write each packet to a separate file to a folder on disk for debugging and/or reverse engineering purposes
    /// </summary>
    /// <remarks>
    /// <para>You would register this class in the <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/> when you want to debug Protobuf message formats.
    /// Depending on the message type the first 10 messages will be stored, you can control that limit using <see cref="SetMaxNUmberOfMessagesForType"/>.</para>
    /// <para>By default the <see cref="NopMessageWriter"/> is registered and no messages are written anywhere.</para>
    /// </remarks>
    public class InvidividualFileMessageWriter : IMessageWriter
    {
        private readonly Dictionary<string, int> _messageTypeCounters = new();
        private readonly Dictionary<string, int> _messageTypeLimits = new();
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
            uint sequenceNummber)
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

            // Only store a message when the limit for that type
            // hasn't been reached yet to prevent a potentially
            // large amount of files being written.
            if (_messageTypeCounters[type] < GetMaxNumberOfMessagesForType(type))
            {
                File.WriteAllBytes(
                    $"{_outputPath}\\{sequenceNummber:000000}-{direction.ToString().ToLower()}-{messageType:000}.bin",
                    buffer);

                _messageTypeCounters[type]++;
            }
        }

        private int GetMaxNumberOfMessagesForType(string type)
        {
            if (_messageTypeLimits.ContainsKey(type))
            {
                return _messageTypeLimits[type];
            }

            return 10;
        }

        public void SetMaxNUmberOfMessagesForType(string type, int count)
        {
            if (!_messageTypeLimits.ContainsKey(type))
            {
                _messageTypeLimits.Add(type, count);
            }
            else
            {
                _messageTypeLimits[type] = count;
            }
        }

        public string GetSummary()
        {
            return string.Join(
                Environment.NewLine,
                _messageTypeCounters
                    .Select(kv => $"{kv.Key.PadLeft(20)}: {kv.Value:#######}")
                    .ToList());
        }
    }
}