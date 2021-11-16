using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ZwiftPacketMonitor.Replay
{
    class DependencyRegistration
    {
        public static ServiceProvider Register()
        {
            var serviceCollection = new ServiceCollection();

            // added to allow logging level configuration, change MinLevel to LogLevel.Debug to see internal traces.
            serviceCollection.AddLogging(configure => configure.AddConsole())
                .Configure<LoggerFilterOptions>(configure => configure.MinLevel = LogLevel.Information);

            serviceCollection.AddZwiftPacketMonitoring();

            serviceCollection.AddSingleton<InvidividualFileMessageWriter>();
            serviceCollection.AddSingleton<IMessageWriter, InvidividualFileMessageWriter>();

            var serviceProvider = serviceCollection.BuildServiceProvider();
            return serviceProvider;
        }
    }
}