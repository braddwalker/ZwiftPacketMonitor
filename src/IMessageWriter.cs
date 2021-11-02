namespace ZwiftPacketMonitor
{
    public interface IMessageWriter
    {
        void OutputTo(string outputPath);

        void StoreMessageType(
            uint messageType, 
            byte[] buffer, 
            Direction direction, 
            uint sequenceNummber);

        void SetMaxNUmberOfMessagesForType(string type, int count);
        string GetSummary();
    }
}