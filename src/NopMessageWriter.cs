namespace ZwiftPacketMonitor
{
    public class NopMessageWriter : IMessageWriter
    {
        public void OutputTo(string outputPath)
        {
        }

        public void StoreMessageType(uint messageType, byte[] buffer, Direction direction, uint sequenceNummber)
        {
        }

        public void SetMaxNUmberOfMessagesForType(string type, int count)
        {
        }

        public string GetSummary()
        {
            return string.Empty;
        }
    }
}