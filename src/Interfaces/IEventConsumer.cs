namespace Ujamaa.Events.Interfaces
{
    /// <summary>
    /// Interface para consumidores de eventos do gerenciador de filas
    /// </summary>
    public interface IEventConsumer
    {
        /// <summary>
        /// Processa um evento recebido do gerenciador de filas
        /// </summary>
        /// <param name="body">Corpo da mensagem recebida</param>
        Task EventProcess(string body);
    }
}