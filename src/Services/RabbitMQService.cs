using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Ujamaa.Events.Interfaces;
using Ujamaa.Events.Models;

namespace Ujamaa.Events.Services
{
    /// <inheritdoc/>
    public class RabbitMQService : IEventService, IAsyncDisposable
    {
        private const ushort PrefetchCount = 30;

        private static readonly ActivitySource ActivitySource = new("Ujamaa.Events.RabbitMQ", "1.0.0");
        private readonly ConnectionFactory _factory;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly SemaphoreSlim _publishLock = new(1, 1);
        private readonly HashSet<string> _declaredQueues = new();
        private readonly HashSet<string> _declaredExchanges = new();
        private IConnection? _connection;
        private IChannel? _publishChannel;

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
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };
        }

        /// <inheritdoc/>
        public async Task Consumer(string queue, IEventConsumer eventConsumer)
        {
            IChannel channel = await GetChannel();

            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false);

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
                    await NackSafely(channel, ea);
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

            await _publishLock.WaitAsync();
            try
            {
                IChannel channel = await GetPublishChannel();

                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: queue ?? string.Empty,
                    mandatory: false,
                    basicProperties: properties,
                    body: body
                );
            }
            finally
            {
                _publishLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task CreateQueue(string queue)
        {
            if (!_declaredQueues.Add(queue))
            {
                return;
            }

            string deadLetterExchange = $"{queue}.dlx";
            string deadLetterQueue = $"{queue}.dlq";

            try
            {
                IChannel channel = await GetChannel();
                await DeclareDeadLetterTopology(channel, deadLetterExchange, deadLetterQueue);

                await channel.QueueDeclareAsync(
                    queue: queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-dead-letter-exchange"] = deadLetterExchange
                    }
                );
            }
            catch (OperationInterruptedException)
            {
                // Fila já existente sem dead-letter: mantém compatível em vez de derrubar o boot.
                IChannel fallback = await GetChannel();
                await fallback.QueueDeclareAsync(
                    queue: queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );
            }
            catch
            {
                _declaredQueues.Remove(queue);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task CreateExchange(string exchange, string type = ExchangeType.Fanout)
        {
            if (!_declaredExchanges.Add($"{exchange}:{type}"))
            {
                return;
            }

            try
            {
                IChannel channel = await GetChannel();

                await channel.ExchangeDeclareAsync(
                    exchange: exchange,
                    type: type
                );
            }
            catch
            {
                _declaredExchanges.Remove($"{exchange}:{type}");
                throw;
            }
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

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_publishChannel != null)
            {
                await _publishChannel.DisposeAsync();
            }

            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }

            _connectionLock.Dispose();
            _publishLock.Dispose();
        }

        private static async Task DeclareDeadLetterTopology(IChannel channel, string deadLetterExchange, string deadLetterQueue)
        {
            await channel.ExchangeDeclareAsync(
                exchange: deadLetterExchange,
                type: ExchangeType.Fanout
            );

            await channel.QueueDeclareAsync(
                queue: deadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            await channel.QueueBindAsync(
                queue: deadLetterQueue,
                exchange: deadLetterExchange,
                routingKey: string.Empty
            );
        }

        private static async Task NackSafely(IChannel channel, BasicDeliverEventArgs ea)
        {
            try
            {
                // Primeira falha: requeue pra tentar de novo. Já redelivered: dead-letter (DLQ).
                await channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: !ea.Redelivered);
            }
            catch
            {
                // Canal morto: a mensagem volta pra fila quando a conexão se recuperar.
            }
        }

        private async Task<IConnection> GetConnection()
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            await _connectionLock.WaitAsync();
            try
            {
                if (_connection is { IsOpen: true })
                {
                    return _connection;
                }

                if (_connection != null)
                {
                    await _connection.DisposeAsync();
                }

                _connection = await _factory.CreateConnectionAsync();
                return _connection;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task<IChannel> GetChannel()
        {
            IConnection connection = await GetConnection();
            return await connection.CreateChannelAsync();
        }

        private async Task<IChannel> GetPublishChannel()
        {
            if (_publishChannel is { IsOpen: true })
            {
                return _publishChannel;
            }

            if (_publishChannel != null)
            {
                await _publishChannel.DisposeAsync();
            }

            _publishChannel = await GetChannel();
            return _publishChannel;
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
