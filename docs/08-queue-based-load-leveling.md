# Queue-Based Load Leveling Pattern

## Overview

The Queue-Based Load Leveling pattern uses a message queue between services to decouple processing and handle traffic spikes. This pattern ensures that services can continue to function even when the system experiences variable loads or when downstream services are temporarily unavailable.

## Problem Statement

When services communicate directly, a sudden increase in traffic can overload the receiving service, potentially causing it to fail. Additionally, if a service becomes temporarily unavailable, all incoming requests during that time are lost, affecting the overall system reliability.

## Solution: Queue-Based Load Leveling

Insert a message queue between services that need to communicate. The sending service places messages on the queue rather than calling the receiving service directly. The receiving service processes messages at its own pace, effectively decoupling the two services.

## Benefits

- Absorbs spikes in workload, preventing service overload
- Improves system resilience during temporary service outages
- Decouples services, simplifying scaling and deployment
- Provides a buffer against traffic surges
- Enables asynchronous processing

## Implementation Considerations

- Choose an appropriate queue technology (Azure Service Bus, RabbitMQ, AWS SQS, etc.)
- Implement proper error handling and dead-letter queues
- Consider message ordering requirements and delivery guarantees
- Design messages to be self-contained and include relevant context
- Implement proper retry policies for message processing

## Code Example: Queue-Based Load Leveling in C#

```csharp
// Sender Service - Order Processing API
public class OrderController : Controller
{
    private readonly IQueueService _queueService;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IQueueService queueService, ILogger<OrderController> logger)
    {
        _queueService = queueService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> SubmitOrder([FromBody] OrderDto order)
    {
        try
        {
            // Validate the order
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Generate an order ID
            order.Id = Guid.NewGuid().ToString();
            order.Status = OrderStatus.Pending;
            order.Timestamp = DateTime.UtcNow;

            // Queue the order for processing instead of processing it directly
            await _queueService.SendMessageAsync("orders", order);

            _logger.LogInformation($"Order {order.Id} queued for processing");

            // Return immediately with a tracking ID
            return Ok(new { 
                OrderId = order.Id, 
                Message = "Order has been accepted for processing"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue order");
            return StatusCode(500, "An error occurred while processing your order");
        }
    }
}

// Message Queue Service Implementation using Azure Service Bus
public class AzureServiceBusQueueService : IQueueService
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AzureServiceBusQueueService> _logger;
    private readonly Dictionary<string, ServiceBusSender> _senders = new();

    public AzureServiceBusQueueService(
        string connectionString,
        ILogger<AzureServiceBusQueueService> logger)
    {
        _client = new ServiceBusClient(connectionString);
        _logger = logger;
    }

    public async Task SendMessageAsync<T>(string queueName, T message)
    {
        if (!_senders.TryGetValue(queueName, out var sender))
        {
            sender = _client.CreateSender(queueName);
            _senders[queueName] = sender;
        }

        // Serialize the message
        var messageBody = JsonSerializer.Serialize(message);
        var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json"
        };

        // Send with retry policy
        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, timeSpan, context) =>
                {
                    _logger.LogWarning(exception, 
                        "Error sending message to queue {QueueName}. Retrying after {RetryTimeSpan}.", 
                        queueName, timeSpan);
                });

        await retryPolicy.ExecuteAsync(async () =>
        {
            await sender.SendMessageAsync(serviceBusMessage);
            _logger.LogInformation("Message sent to queue {QueueName}", queueName);
        });
    }
}

// Receiver Service - Background Worker processing orders
public class OrderProcessingService : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusProcessor _processor;
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        string connectionString,
        IOrderRepository orderRepository,
        ILogger<OrderProcessingService> logger)
    {
        _client = new ServiceBusClient(connectionString);
        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 10,
            AutoCompleteMessages = false
        };
        _processor = _client.CreateProcessor("orders", options);
        _orderRepository = orderRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessOrderMessage;
        _processor.ProcessErrorAsync += ProcessError;

        await _processor.StartProcessingAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        await _processor.StopProcessingAsync(stoppingToken);
    }

    private async Task ProcessOrderMessage(ProcessMessageEventArgs args)
    {
        try
        {
            var body = args.Message.Body.ToString();
            _logger.LogInformation($"Received order message: {body}");

            var order = JsonSerializer.Deserialize<OrderDto>(body);

            // Process the order (e.g., validate, save to database, etc.)
            await _orderRepository.CreateOrderAsync(order);

            // Complete the message to remove it from the queue
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order message");
            
            // Abandon the message to make it available for reprocessing
            // or move to dead-letter queue after multiple failures
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task ProcessError(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error processing message from queue");
        return Task.CompletedTask;
    }
}
```

## Code Example: Queue-Based Load Leveling in TypeScript

```typescript
// Using AWS SQS in a Node.js environment

import { SQSClient, SendMessageCommand, ReceiveMessageCommand, DeleteMessageCommand } from '@aws-sdk/client-sqs';

// Message Producer - Order API
export class OrderService {
  private sqsClient: SQSClient;
  private queueUrl: string;

  constructor(region: string, queueUrl: string) {
    this.sqsClient = new SQSClient({ region });
    this.queueUrl = queueUrl;
  }

  async submitOrder(order: Order): Promise<{ orderId: string }> {
    // Generate an order ID and timestamp
    const orderId = crypto.randomUUID();
    const timestamp = new Date().toISOString();

    // Prepare the order for queueing
    const orderMessage = {
      ...order,
      id: orderId,
      status: 'PENDING',
      timestamp
    };

    // Send to queue instead of processing immediately
    try {
      const command = new SendMessageCommand({
        QueueUrl: this.queueUrl,
        MessageBody: JSON.stringify(orderMessage),
        MessageAttributes: {
          'MessageType': {
            DataType: 'String',
            StringValue: 'Order'
          }
        }
      });

      await this.sqsClient.send(command);
      console.log(`Order ${orderId} queued for processing`);
      
      return { orderId };
    } catch (error) {
      console.error('Failed to queue order:', error);
      throw new Error('Failed to process order');
    }
  }
}

// Message Consumer - Order Processing Worker
export class OrderProcessor {
  private sqsClient: SQSClient;
  private queueUrl: string;
  private orderRepository: OrderRepository;
  private isRunning: boolean = false;

  constructor(
    region: string, 
    queueUrl: string,
    orderRepository: OrderRepository
  ) {
    this.sqsClient = new SQSClient({ region });
    this.queueUrl = queueUrl;
    this.orderRepository = orderRepository;
  }

  async start(): Promise<void> {
    if (this.isRunning) return;
    
    this.isRunning = true;
    console.log('Order processor started');
    
    while (this.isRunning) {
      try {
        await this.processNextBatch();
      } catch (error) {
        console.error('Error processing message batch:', error);
        // Add a delay before retrying to prevent tight loops on persistent errors
        await new Promise(resolve => setTimeout(resolve, 5000));
      }
    }
  }

  stop(): void {
    this.isRunning = false;
    console.log('Order processor stopping');
  }

  private async processNextBatch(): Promise<void> {
    const command = new ReceiveMessageCommand({
      QueueUrl: this.queueUrl,
      MaxNumberOfMessages: 10,
      WaitTimeSeconds: 20, // Long polling
      MessageAttributeNames: ['All'],
      AttributeNames: ['All']
    });

    const response = await this.sqsClient.send(command);

    if (!response.Messages || response.Messages.length === 0) {
      return; // No messages to process
    }

    for (const message of response.Messages) {
      try {
        if (!message.Body) continue;
        
        const order = JSON.parse(message.Body) as Order;
        console.log(`Processing order ${order.id}`);

        // Process the order
        await this.orderRepository.saveOrder(order);

        // Update order status
        await this.orderRepository.updateOrderStatus(order.id, 'PROCESSING');

        // Delete the message from the queue after successful processing
        const deleteCommand = new DeleteMessageCommand({
          QueueUrl: this.queueUrl,
          ReceiptHandle: message.ReceiptHandle
        });
        await this.sqsClient.send(deleteCommand);
        
        console.log(`Order ${order.id} processed successfully`);
      } catch (error) {
        console.error(`Error processing message: ${message.MessageId}`, error);
        // In a real implementation, we might implement a dead-letter queue
        // or custom error handling logic based on the type of error
      }
    }
  }
}

// Usage example
async function main() {
  // Initialize the Order Processor in a separate service/worker
  const orderProcessor = new OrderProcessor(
    'us-east-1',
    'https://sqs.us-east-1.amazonaws.com/123456789012/order-queue',
    new OrderRepository()
  );
  
  // Start processing in the background
  orderProcessor.start();
  
  // Handle graceful shutdown
  process.on('SIGINT', () => {
    console.log('Shutting down...');
    orderProcessor.stop();
    process.exit(0);
  });
}

// Start the processor
main().catch(console.error);
```

## When to Use

- When a service experiences variable load or traffic spikes
- To improve system resilience during temporary service outages
- When the rate of requests exceeds the capacity of the receiving service
- For long-running operations that don't require immediate response
- When processing can be done asynchronously

## When Not to Use

- For operations requiring immediate response
- When message ordering is critical and difficult to maintain
- For simple systems with stable, predictable workloads
- When the overhead of queue management outweighs the benefits

## Related Patterns

- [Retry Pattern](02-retry-pattern.md)
- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Throttling & Rate Limiting](08-throttling-rate-limiting.md)
