# Ujamaa Events

Uma biblioteca .NET desenvolvida para abstrair e facilitar a comunicação com serviços de mensageria e filas de eventos.

## Sobre

Esta biblioteca foi criada para fornecer uma camada de abstração robusta para comunicação com sistemas de filas. Por razões arquiteturais, adotamos o RabbitMQ como nossa implementação padrão para gerenciar filas de eventos. No entanto, seguindo princípios de design sólidos, a biblioteca depende de interfaces bem definidas, permitindo que qualquer implementação que respeite nossos contratos possa ser utilizada.

## Características

- ✅ Abstração de serviços de mensageria
- ✅ Implementação padrão com RabbitMQ
- ✅ Design baseado em interfaces para máxima flexibilidade
- ✅ Fácil integração e configuração
- ✅ Suporte a padrões de consumo de eventos

## Arquitetura

A biblioteca é estruturada seguindo os princípios de inversão de dependência, garantindo que o código cliente dependa apenas de abstrações, não de implementações concretas. Isso facilita testes, manutenção e possíveis migrações futuras.

## Como usar

### 1. Configuração do Ambiente

Primeiro, configure as variáveis de ambiente da sua aplicação. No exemplo abaixo, estamos carregando as variáveis do `appsettings.json`, mas você pode carregá-las de qualquer fonte: variáveis do sistema operacional, AWS Secrets Manager, AWS Parameter Store, Azure Key Vault, etc.

```csharp
// No Program.cs ou Startup.cs
builder.Services.Configure<AMQPEnv>(builder.Configuration.GetSection("AMQP"));

// Registra os serviços da biblioteca
builder.Services.UjamaaEventsInjection();
```

### 2. Configuração do appsettings.json

```json
{
  "AMQP": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "test",
    "Password": "test",
    "VirtualHost": "/",
    "ClientProvidedName": "test"
  }
}
```

### 3. Implementação do Consumidor

Implemente a interface `IEventConsumer` para processar as mensagens da fila:

```csharp
public class MyConcreteConsumer : IEventConsumer 
{
    public async Task EventProcess(string body)
    {
        // Sua lógica de processamento aqui
        Console.WriteLine($"Mensagem recebida com sucesso: {body}");
        
        // Exemplo: deserializar JSON
        // var data = JsonSerializer.Deserialize<MyEventData>(body);
        // await ProcessEvent(data);
    }
}
```

### 4. Uso dos Serviços

```csharp
// Injeção dos serviços
var eventService = services.GetRequiredService<IEventService>();
var consumer = services.GetRequiredService<MyConcreteConsumer>();

// Criação da fila
await eventService.CreateQueue("my-queue");

// Configuração do consumidor
await eventService.Consumer("my-queue", consumer);

// Publicação de mensagens
var tipoEvento = new TipoEvento();
await eventService.Publish<TipoEvento>(tipoEvento, "exchange", "my-queue");
```

### 5. Exemplo Completo

```csharp
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Configuração
        builder.Services.Configure<AMQPEnv>(builder.Configuration.GetSection("AMQP"));
        builder.Services.UjamaaEventsInjection();
        builder.Services.AddTransient<MyConcreteConsumer>();
        
        var app = builder.Build();
        
        // Uso
        var eventService = app.Services.GetRequiredService<IEventService>();
        var consumer = app.Services.GetRequiredService<MyConcreteConsumer>();
        
        await eventService.CreateQueue("orders-queue");
        await eventService.Consumer("orders-queue", consumer);
        
        app.Run();
    }
}
```



