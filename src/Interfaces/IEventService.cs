using RabbitMQ.Client;

namespace Ujamaa.Events.Interfaces
{
    /// <summary>
    /// Interface para serviços manipuladores de eventos do gerenciador de filas
    /// </summary>
    public interface IEventService
    {
        /// <summary>
        /// Envia dados para uma fila específica
        /// </summary>
        /// <typeparam name="T">Tipo dos dados a serem enviados</typeparam>
        /// <param name="data">Dados a serem enviados</param>
        /// <param name="exchange">Nome da exchange para envio</param>
        /// <param name="queue">Nome da fila para envio</param>
        ValueTask Send<T>(T data, string exchange, string queue) where T : class;

        /// <summary>
        /// Consome eventos de uma fila específica
        /// </summary>
        /// <param name="queue">Nome da fila a ser consumida</param>
        /// <param name="eventConsumer">Instância do consumidor de eventos</param>
        Task Consumer(string queue, IEventConsumer eventConsumer);

        /// <summary>
        /// Vincula uma fila a uma exchange específica
        /// </summary>
        /// <param name="queue">Nome da fila a ser vinculada</param>
        /// <param name="exchange">Nome da exchange a ser vinculada</param>
        Task BindQueueToExchange(string queue, string exchange);

        /// <summary>
        /// Cria uma fila com o nome especificado
        /// </summary>
        /// <param name="queue">Nome da fila a ser criada</param>
        Task CreateQueue(string queue);

        /// <summary>
        /// Cria uma exchange com o nome e tipo especificados
        /// </summary>
        /// <param name="exchange">Nome da exchange a ser criada</param>
        /// <param name="type">Tipo da exchange (default é Fanout)</param>
        Task CreateExchange(string exchange, string type = ExchangeType.Fanout);
    }
}