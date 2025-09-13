using Microsoft.Extensions.DependencyInjection;
using Ujamaa.Events.Interfaces;
using Ujamaa.Events.Services;

namespace Ujamaa.Events
{
    /// <summary>
    /// Extensões para a coleção de serviços
    /// </summary>
    public static class IServiceCollectionExtension
    {
        /// <summary>
        /// Configura os serviços events do Ujamaa
        /// </summary>
        /// <param name="services">Coleção de serviços onde os serviços events serão registrados</param>
        /// <returns>Retorna a coleção de serviços configurada</returns>
        public static IServiceCollection UjamaaEventsInjection(this IServiceCollection services)
        {
            services.AddSingleton<IEventService, RabbitMQService>();

            return services;
        }

    }
}