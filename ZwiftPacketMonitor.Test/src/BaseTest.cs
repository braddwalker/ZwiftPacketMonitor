using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;

namespace ZwiftPacketMonitor.Test
{
    [TestClass]
    public abstract class BaseTest
    {
        private ILoggerFactory _loggerFactory;

        [TestInitialize]
        public virtual void Setup() { 
            _loggerFactory = LoggerFactory.Create(builder => builder
                .AddConsole()
                .AddDebug());
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
