namespace Ujamaa.Events.Models
{
    /// <summary>
    /// Representa as configurações do ambiente AMQP (Advanced Message Queuing Protocol).
    /// </summary>
    public class AMQPEnv
    {
        /// <summary>
        /// Host do servidor AMQP.
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Porta do servidor AMQP.
        /// </summary>
        public int Port { get; set; } = 5672;

        /// <summary>
        /// Nome de usuário para autenticação no servidor AMQP.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Senha para autenticação no servidor AMQP.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Host virtual para o servidor AMQP.
        /// </summary>
        public string VirtualHost { get; set; } = string.Empty;

        /// <summary>
        /// Nome fornecido pelo cliente para identificar a conexão.
        /// </summary>
        public string ClientProvidedName { get; set; } = string.Empty;
    }
}