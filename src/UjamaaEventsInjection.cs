using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ujamaa.Events.Interfaces;
using Ujamaa.Events.Models;
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
        /// <param name="configuration">Configurações da aplicação</param>
        /// <returns>Retorna a coleção de serviços configurada</returns>
        public static IServiceCollection UjamaaEventsInjection(this IServiceCollection services, ConfigurationManager configuration)
        {
            services.AddSingleton(
                configuration.GetSection(typeof(AMQPEnv).Name.Replace("Env", string.Empty)).Get<AMQPEnv>()!
            );

            services.AddSingleton<IEventService, RabbitMQService>();

            return services;
        }

    }
}