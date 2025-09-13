using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ujamaa.Events.Interfaces;
using Ujamaa.Events.Models;
using Microsoft.Extensions.Options;


namespace Ujamaa.Events.Services
{
    /// <inheritdoc/>
    public class RabbitMQService : IEventService
    {
        private readonly ConnectionFactory _factory;

        /// <summary>
        /// Inicializa uma nova instância do RabbitMQService.
        /// </summary>
        /// <param name="amqpEnv">Configurações do ambiente AMQP.</param>
        public RabbitMQService(
            IOptions<AMQPEnv> amqpEnv
        )
        {
            AMQPEnv amqp = amqpEnv.Value;
            _factory = new ConnectionFactory
            {
                HostName = amqp.Host,
                Port = amqp.Port,
                UserName = amqp.Username,
                Password = amqp.Password,
                VirtualHost = amqp.VirtualHost,
                ClientProvidedName = "Ujamaa",
            };
        }

        /// <inheritdoc/>
        public async Task Consumer(string queue, IEventConsumer eventConsumer)
        {
            IChannel channel = await GetChannel();

            AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                byte[] body = ea.Body.ToArray();
                string message = Encoding.UTF8.GetString(body);

                await eventConsumer.EventProcess(message);
                await channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
            };

            await channel.BasicConsumeAsync(
                queue: queue,
                autoAck: false,
                consumer: consumer
            );
        }

        /// <inheritdoc/>
        public async ValueTask Send<T>(T data, string exchange, string queue) where T : class
        {

            IChannel channel = await GetChannel();

            if (!string.IsNullOrEmpty(queue))
            {
                await CreateQueue(queue);
            }

            if (!string.IsNullOrEmpty(exchange))
            {
                await CreateExchange(exchange);
            }

            string message = SerializeObject(data);
            byte[] body = Encoding.UTF8.GetBytes(message);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
            };

            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: queue,
                mandatory: false,
                basicProperties: properties,
                body: body
            );
        }

        /// <inheritdoc/>
        public async Task CreateQueue(string queue)
        {
            IChannel channel = await GetChannel();

            await channel.QueueDeclareAsync(
                queue: queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );
        }

        /// <inheritdoc/>
        public async Task CreateExchange(string exchange, string type = ExchangeType.Fanout)
        {
            IChannel channel = await GetChannel();

            await channel.ExchangeDeclareAsync(
                exchange: exchange,
                type: type
            );
        }

        /// <inheritdoc/>
        public async Task BindQueueToExchange(string queue, string exchange)
        {
            IChannel channel = await GetChannel();

            await channel.QueueBindAsync(
                queue: queue,
                exchange: exchange,
                routingKey: string.Empty
            );
        }

        private async Task<IChannel> GetChannel()
        {
            IConnection connection = await _factory.CreateConnectionAsync();
            IChannel channel = await connection.CreateChannelAsync();
            return channel;
        }

        private string SerializeObject<T>(T data) where T : class
        {
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic),
                WriteIndented = true
            };
            return JsonSerializer.Serialize(data, options);
        }
    }
}