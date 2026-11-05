using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ujamaa.Events.Interfaces;
using Ujamaa.Events.Models;

namespace Ujamaa.Events.Services
{
    /// <inheritdoc/>
    public class RabbitMQService : IEventService
    {
        private static readonly ActivitySource ActivitySource = new("Ujamaa.Events.RabbitMQ", "1.0.0");
        private readonly ConnectionFactory _factory;

        /// <summary>
        /// Inicializa uma nova instância do RabbitMQService.
        /// </summary>
        /// <param name="amqp">Configurações do ambiente AMQP.</param>
        public RabbitMQService(
            AMQPEnv amqp
        )
        {
            _factory = new ConnectionFactory
            {
                HostName = amqp.Host,
                Port = amqp.Port,
                UserName = amqp.Username,
                Password = amqp.Password,
                VirtualHost = amqp.VirtualHost,
                ClientProvidedName = amqp.ClientProvidedName,
            };
        }

        /// <inheritdoc/>
        public async Task Consumer(string queue, IEventConsumer eventConsumer)
        {
            IChannel channel = await GetChannel();

            AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                using Activity? activity = StartConsumerActivity(ea);

                try
                {
                    byte[] body = ea.Body.ToArray();
                    string message = Encoding.UTF8.GetString(body);

                    if (!string.IsNullOrEmpty(message))
                    {
                        await eventConsumer.EventProcess(message);
                    }

                    await channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
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
            using Activity? activity = ActivitySource.StartActivity("RabbitMQ.Publish", ActivityKind.Producer);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", queue ?? exchange);

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
                Headers = BuildTraceHeaders(),
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

        private static Dictionary<string, object?>? BuildTraceHeaders()
        {
            var current = Activity.Current;
            if (current == null)
                return null;

            return new Dictionary<string, object?>
            {
                ["traceparent"] = current.Id,
                ["tracestate"] = current.TraceStateString
            };
        }

        private static Activity? StartConsumerActivity(BasicDeliverEventArgs ea)
        {
            var traceParent = GetHeaderString(ea.BasicProperties.Headers, "traceparent");
            var traceState = GetHeaderString(ea.BasicProperties.Headers, "tracestate");

            Activity? activity = null;
            if (!string.IsNullOrWhiteSpace(traceParent)
                && ActivityContext.TryParse(traceParent, traceState, out var parentContext))
            {
                activity = ActivitySource.StartActivity("RabbitMQ.Consume", ActivityKind.Consumer, parentContext);
            }

            // Sem traceparent (mensagem legada): abre span raiz — melhor que perder a telemetria.
            activity ??= ActivitySource.StartActivity("RabbitMQ.Consume", ActivityKind.Consumer);

            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", ea.RoutingKey);

            return activity;
        }

        private static string? GetHeaderString(IDictionary<string, object?>? headers, string key)
        {
            if (headers == null || !headers.TryGetValue(key, out var value))
                return null;

            if (value is string s)
                return s;

            if (value is byte[] bytes)
                return Encoding.UTF8.GetString(bytes);

            return value?.ToString();
        }
    }
}
