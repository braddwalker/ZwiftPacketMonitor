using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace ZwiftPacketMonitor.Test
{
    [TestClass]
    public abstract class BaseTest
    {
        private ILoggerFactory _loggerFactory;

        [TestInitialize]
        public virtual void Setup() { 
            var serviceProvider = new ServiceCollection()
                .AddLogging(x => x.AddDebug().AddConsole())
                .BuildServiceProvider();

            _loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        }

        [TestCleanup]
        public virtual void Teardown() {
        }

        protected ILogger<T> CreateLogger<T>()
        {
            return (_loggerFactory.CreateLogger<T>());
        }
    }
}