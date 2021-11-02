using Microsoft.Extensions.DependencyInjection;

namespace ZwiftPacketMonitor
{
    /// <summary>
    /// Provides extensions for registering services
    /// </summary>
    public static class RegistrationExtensions
    {
        /// <summary>
        /// Registers custom services needed to utilize packet monitoring
        /// </summary>
        /// <param name="services">The collection of service descriptors.</param>
        /// <returns>The collection of service descriptors.</returns>
        public static IServiceCollection AddZwiftPacketMonitoring(this IServiceCollection services)
        {
            services.AddTransient<PacketAssembler>();
            services.AddSingleton<Monitor>();

            return (services);
        }

        /// <summary>
        /// Registeres the <see cref="CompanionPacketDecoder"/> so that Zwift Companion app packets are captured
        /// </summary>
        /// <param name="services">The collection of service descriptors.</param>
        /// <returns>The collection of service descriptors.</returns>
        public static IServiceCollection AddZwiftCompanionPacketMonitoring(this IServiceCollection services)
        {
            services.AddSingleton<CompanionPacketDecoder>();

            return services;
        }
    }
}