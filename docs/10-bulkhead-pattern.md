# Bulkhead Pattern for Azure Applications

## What is the Bulkhead Pattern?

The Bulkhead Pattern isolates elements of an application into pools so that if one fails, the others continue to function. The term comes from ship construction, where a ship's hull is divided into separate watertight compartments (bulkheads) to prevent a single breach from sinking the entire vessel.

## Why is the Bulkhead Pattern Important?

In cloud applications, the Bulkhead Pattern:

1. **Prevents Cascading Failures**: Isolates failures to specific components
2. **Preserves Functionality**: Keeps critical services running even when non-critical ones fail
3. **Enables Graceful Degradation**: Allows the system to provide limited functionality during partial outages
4. **Protects Shared Resources**: Prevents one consumer from exhausting all available resources
5. **Improves Resilience**: Creates natural boundaries for failure isolation

## Implementation Strategies

### 1. Thread Pool Isolation

**Why**: Separate thread pools prevent one slow component from consuming all available threads.

**Guidelines**:
- Create dedicated thread pools for different components or operations
- Size thread pools based on the importance and characteristics of each component
- Monitor thread pool utilization and adjust as needed

**C# Example Using ThreadPool Isolation**:
```csharp
// Implementing thread pool isolation with custom thread pools
public class BulkheadedService
{
    private readonly SemaphoreSlim _criticalServiceSemaphore;
    private readonly SemaphoreSlim _nonCriticalServiceSemaphore;
    private readonly ILogger<BulkheadedService> _logger;
    
    public BulkheadedService(ILogger<BulkheadedService> logger)
    {
        // Create separate "pools" with different capacities
        _criticalServiceSemaphore = new SemaphoreSlim(20); // Higher capacity for critical operations
        _nonCriticalServiceSemaphore = new SemaphoreSlim(10); // Lower capacity for non-critical operations
        _logger = logger;
    }
    
    public async Task<TResult> ExecuteCriticalOperationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try to enter the bulkhead - if full, throws BulkheadRejectedException
            if (!await _criticalServiceSemaphore.WaitAsync(
                TimeSpan.FromSeconds(2), cancellationToken))
            {
                _logger.LogWarning("Critical service bulkhead rejected operation - at capacity");
                throw new BulkheadRejectedException("Critical service bulkhead is full");
            }
            
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                _criticalServiceSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Critical operation was cancelled");
            throw;
        }
        catch (Exception ex) when (!(ex is BulkheadRejectedException))
        {
            _logger.LogError(ex, "Error in critical operation");
            throw;
        }
    }
    
    public async Task<TResult> ExecuteNonCriticalOperationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            // For non-critical operations, we might use a shorter timeout
            // and more readily reject or return a default value
            if (!await _nonCriticalServiceSemaphore.WaitAsync(
                TimeSpan.FromSeconds(1), cancellationToken))
            {
                _logger.LogWarning("Non-critical service bulkhead rejected operation - at capacity");
                
                // For non-critical operations, we might return a default value instead of throwing
                return default;
            }
            
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                _nonCriticalServiceSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Non-critical operation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in non-critical operation");
            // For non-critical operations, consider returning a default value
            return default;
        }
    }
    
    // Custom exception for bulkhead rejection
    public class BulkheadRejectedException : Exception
    {
        public BulkheadRejectedException(string message) : base(message) { }
    }
}

// Usage example
public class OrderService
{
    private readonly BulkheadedService _bulkhead;
    private readonly IOrderRepository _orderRepository;
    private readonly IRecommendationService _recommendationService;
    private readonly ILogger<OrderService> _logger;
    
    public OrderService(
        BulkheadedService bulkhead,
        IOrderRepository orderRepository,
        IRecommendationService recommendationService,
        ILogger<OrderService> logger)
    {
        _bulkhead = bulkhead;
        _orderRepository = orderRepository;
        _recommendationService = recommendationService;
        _logger = logger;
    }
    
    public async Task<OrderDetailsViewModel> GetOrderDetailsAsync(
        string orderId, 
        CancellationToken cancellationToken)
    {
        // Get critical order data using the critical bulkhead
        var orderDetails = await _bulkhead.ExecuteCriticalOperationAsync(
            async ct => await _orderRepository.GetOrderAsync(orderId, ct),
            cancellationToken);
            
        // Get recommendations using the non-critical bulkhead
        var recommendations = await _bulkhead.ExecuteNonCriticalOperationAsync(
            async ct => await _recommendationService.GetRecommendationsAsync(orderDetails.CustomerId, ct),
            cancellationToken);
            
        // If the recommendations call was rejected or failed, use empty list
        recommendations ??= Array.Empty<ProductRecommendation>();
        
        return new OrderDetailsViewModel
        {
            Order = orderDetails,
            RecommendedProducts = recommendations
        };
    }
}
```

### 2. Connection Pool Isolation

**Why**: Separate connection pools prevent one component from exhausting database or service connections.

**Guidelines**:
- Create separate connection pools for different services or operations
- Configure appropriate connection limits for each pool
- Implement connection timeouts to prevent waiting indefinitely for connections

**Example for Azure SQL Database Connection Isolation (C#)**:
```csharp
public class ConnectionPoolBulkhead
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConnectionPoolBulkhead> _logger;
    
    // Store connection strings for different contexts to ensure separate pools
    private readonly string _criticalDbConnectionString;
    private readonly string _reportingDbConnectionString;
    private readonly string _loggingDbConnectionString;
    
    public ConnectionPoolBulkhead(IConfiguration configuration, ILogger<ConnectionPoolBulkhead> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Get base connection string
        var baseConnectionString = _configuration.GetConnectionString("SqlDatabase");
        
        // Create connection string variants to ensure separate pools
        // Add a different Application Name parameter to each connection string
        // ADO.NET creates separate pools for different connection strings
        _criticalDbConnectionString = ModifyConnectionString(baseConnectionString, "CriticalOperations");
        _reportingDbConnectionString = ModifyConnectionString(baseConnectionString, "ReportingOperations");
        _loggingDbConnectionString = ModifyConnectionString(baseConnectionString, "LoggingOperations");
    }
    
    private string ModifyConnectionString(string connectionString, string applicationName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName,
            
            // Set pool-specific settings
            // Note: these settings should be tuned based on the usage patterns
            MinPoolSize = applicationName == "CriticalOperations" ? 10 : 1,
            MaxPoolSize = applicationName == "CriticalOperations" ? 100 : 30,
            ConnectTimeout = applicationName == "CriticalOperations" ? 15 : 30,
            LoadBalanceTimeout = applicationName == "CriticalOperations" ? 30 : 60
        };
        
        return builder.ConnectionString;
    }
    
    public SqlConnection GetCriticalConnection()
    {
        _logger.LogDebug("Creating SQL connection from critical pool");
        return new SqlConnection(_criticalDbConnectionString);
    }
    
    public SqlConnection GetReportingConnection()
    {
        _logger.LogDebug("Creating SQL connection from reporting pool");
        return new SqlConnection(_reportingDbConnectionString);
    }
    
    public SqlConnection GetLoggingConnection()
    {
        _logger.LogDebug("Creating SQL connection from logging pool");
        return new SqlConnection(_loggingDbConnectionString);
    }
    
    // Helper method to execute a database operation within the critical pool
    public async Task<T> ExecuteCriticalDatabaseOperationAsync<T>(
        Func<SqlConnection, Task<T>> operation, 
        CancellationToken cancellationToken)
    {
        using var connection = GetCriticalConnection();
        await connection.OpenAsync(cancellationToken);
        return await operation(connection);
    }
    
    // Helper method to execute a database operation within the reporting pool
    public async Task<T> ExecuteReportingDatabaseOperationAsync<T>(
        Func<SqlConnection, Task<T>> operation, 
        CancellationToken cancellationToken)
    {
        using var connection = GetReportingConnection();
        await connection.OpenAsync(cancellationToken);
        return await operation(connection);
    }
}
```

### 3. Process Isolation

**Why**: Separate processes provide strong isolation between components.

**Guidelines**:
- Deploy critical components in separate services or containers
- Use resource governance to limit CPU, memory, and other resources
- Implement health-based scaling to adjust capacity based on workload

**Azure App Service Example**:
```csharp
// This is an example of how to structure your application for process-level isolation
// Each component would be deployed to its own App Service instance

// Program.cs for OrderProcessingService
public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                
                // Configure App Service instance-specific settings
                webBuilder.UseAzureAppConfiguration();
                
                // Set resource limits for this process
                // In App Service, this is configured via the app service plan
            });
}

// Startup.cs for OrderProcessingService
public class Startup
{
    // ...
    
    public void ConfigureServices(IServiceCollection services)
    {
        // Configure this micro-service with its own resource limits
        services.AddHttpClient("PaymentService", client =>
        {
            client.BaseAddress = new Uri(Configuration["PaymentService:BaseUrl"]);
            
            // Add resilience policies specific to this service-to-service communication
            // These can be different than other services based on criticality
        })
        .AddPolicyHandler(GetCircuitBreakerPolicy())
        .AddPolicyHandler(GetRetryPolicy())
        .AddPolicyHandler(GetTimeoutPolicy());
        
        // Configure other service dependencies
    }
    
    // ...
}

// Deployment script (e.g., ARM template or Azure CLI commands)
// This shows how to create separate App Service instances for different components
// az group create --name resilient-app --location eastus
// 
// az appservice plan create --name critical-plan --resource-group resilient-app --sku P1V2
// az appservice plan create --name non-critical-plan --resource-group resilient-app --sku S1
// 
// az webapp create --name order-processing --resource-group resilient-app --plan critical-plan
// az webapp create --name inventory-service --resource-group resilient-app --plan critical-plan
// az webapp create --name recommendation-service --resource-group resilient-app --plan non-critical-plan
// az webapp create --name analytics-service --resource-group resilient-app --plan non-critical-plan
```

### 4. Client/Endpoint Isolation

**Why**: Isolating clients prevents issues with one service from affecting requests to other services.

**Guidelines**:
- Create separate HttpClient instances for different services
- Configure client policies based on the criticality of the service
- Set independent connection limits and timeouts for each client

**TypeScript Example with Axios**:
```typescript
// Creating isolated HTTP clients for different services
import axios, { AxiosInstance, AxiosRequestConfig } from 'axios';

// Factory for creating isolated HTTP clients
class HttpClientFactory {
  // Create clients for different service categories
  createCriticalServiceClient(baseURL: string): AxiosInstance {
    const config: AxiosRequestConfig = {
      baseURL,
      timeout: 5000, // Short timeout for critical services
      maxRedirects: 2, // Limit redirects
      headers: {
        'X-Service-Category': 'Critical'
      }
    };
    
    const client = axios.create(config);
    
    // Add resilience policies specifically for critical services
    this.addCriticalServicePolicies(client);
    
    return client;
  }
  
  createNonCriticalServiceClient(baseURL: string): AxiosInstance {
    const config: AxiosRequestConfig = {
      baseURL,
      timeout: 10000, // Longer timeout for non-critical services
      maxRedirects: 3,
      headers: {
        'X-Service-Category': 'NonCritical'
      }
    };
    
    const client = axios.create(config);
    
    // Add resilience policies specifically for non-critical services
    this.addNonCriticalServicePolicies(client);
    
    return client;
  }
  
  private addCriticalServicePolicies(client: AxiosInstance): void {
    // Add interceptors for retry, circuit breaking, etc.
    // Example: retry policy with limited attempts
    client.interceptors.response.use(
      response => response,
      async error => {
        const config = error.config;
        
        // Only retry a limited number of times for critical services
        if (!config || !config.headers || config.headers['x-retry'] >= 2) {
          return Promise.reject(error);
        }
        
        // Set retry count
        config.headers['x-retry'] = (config.headers['x-retry'] || 0) + 1;
        
        // Use a shorter delay for critical services
        const delay = 200 * config.headers['x-retry'];
        await new Promise(resolve => setTimeout(resolve, delay));
        
        return client(config);
      }
    );
  }
  
  private addNonCriticalServicePolicies(client: AxiosInstance): void {
    // Add different interceptors for non-critical services
    // Example: more aggressive retry policy
    client.interceptors.response.use(
      response => response,
      async error => {
        const config = error.config;
        
        // Allow more retries for non-critical services
        if (!config || !config.headers || config.headers['x-retry'] >= 5) {
          return Promise.reject(error);
        }
        
        // Set retry count
        config.headers['x-retry'] = (config.headers['x-retry'] || 0) + 1;
        
        // Use exponential backoff with jitter
        const backoff = Math.pow(2, config.headers['x-retry']) * 100;
        const jitter = Math.random() * 100;
        const delay = backoff + jitter;
        
        await new Promise(resolve => setTimeout(resolve, delay));
        
        return client(config);
      }
    );
  }
}

// Usage in application
class OrderService {
  private paymentClient: AxiosInstance;
  private inventoryClient: AxiosInstance;
  private analyticsClient: AxiosInstance;
  
  constructor(private httpClientFactory: HttpClientFactory) {
    // Create isolated clients for different services
    this.paymentClient = httpClientFactory.createCriticalServiceClient(
      'https://payment-api.example.com'
    );
    
    this.inventoryClient = httpClientFactory.createCriticalServiceClient(
      'https://inventory-api.example.com'
    );
    
    this.analyticsClient = httpClientFactory.createNonCriticalServiceClient(
      'https://analytics-api.example.com'
    );
  }
  
  async processOrder(order: Order): Promise<OrderResult> {
    try {
      // Process payment first (critical operation)
      const paymentResult = await this.paymentClient.post('/process', {
        orderId: order.id,
        amount: order.total,
        paymentMethod: order.paymentMethod
      });
      
      // Update inventory (critical operation)
      const inventoryResult = await this.inventoryClient.post('/allocate', {
        orderId: order.id,
        items: order.items
      });
      
      // Send analytics event (non-critical operation)
      // If this fails, it shouldn't affect the order processing
      try {
        await this.analyticsClient.post('/events', {
          type: 'order_completed',
          orderId: order.id,
          total: order.total,
          items: order.items.length
        });
      } catch (error) {
        console.warn('Failed to send analytics event, but order was processed successfully');
        // Non-critical failure, continue processing
      }
      
      return {
        success: true,
        orderId: order.id,
        paymentId: paymentResult.data.paymentId,
        inventoryStatus: inventoryResult.data.status
      };
    } catch (error) {
      // Handle critical path failures
      console.error('Order processing failed:', error);
      throw error;
    }
  }
}
```

## Service-Specific Bulkhead Implementations

### Azure SQL Database

**Strategies**:
- Use elastic pools with resource governance for separate workloads
- Implement separate connection pools for different workloads
- Use Azure SQL Database's resource governor for internal workload isolation

**Example Database Configuration**:
```sql
-- Create a new resource pool for reporting workloads
EXEC sp_xtp_control_proc_exec_stats 1;

CREATE RESOURCE POOL ReportingPool
WITH (
    MIN_CPU_PERCENT = 0,
    MAX_CPU_PERCENT = 40,
    CAP_CPU_PERCENT = 40,
    AFFINITY SCHEDULER = AUTO
);

-- Create a workload group for reporting queries
CREATE WORKLOAD GROUP ReportingWorkloadGroup
WITH (
    IMPORTANCE = LOW,
    REQUEST_MAX_MEMORY_GRANT_PERCENT = 25,
    REQUEST_MAX_CPU_TIME_SEC = 180,
    REQUEST_MEMORY_GRANT_TIMEOUT_SEC = 60,
    MAX_DOP = 4,
    GROUP_MAX_REQUESTS = 100
)
USING ReportingPool;

-- Create a classifier function to route queries to the right workload group
CREATE FUNCTION dbo.WorkloadClassifier()
RETURNS sysname
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @workloadGroup sysname;
    
    -- Check for reporting user or application
    IF (APP_NAME() LIKE '%Reporting%' OR SUSER_NAME() = 'ReportingUser')
    BEGIN
        SET @workloadGroup = 'ReportingWorkloadGroup';
    END
    ELSE
    BEGIN
        SET @workloadGroup = 'default';
    END
    
    RETURN @workloadGroup;
END;

-- Register the classifier
CREATE WORKLOAD CLASSIFIER ReportingClassifier
WITH (
    WORKLOAD_GROUP = 'ReportingWorkloadGroup',
    MEMBERNAME = 'dbo.WorkloadClassifier',
    IMPORTANCE = NORMAL
);

-- Enable Resource Governor
ALTER RESOURCE GOVERNOR RECONFIGURE;
```

### Azure Cosmos DB

**Strategies**:
- Use separate containers for different workloads
- Configure different throughput (RU/s) limits for different containers
- Implement partition keys to distribute load evenly

**Implementation Example (C#)**:
```csharp
// Cosmos DB bulkhead implementation with separate clients and containers
public class CosmosDbBulkhead
{
    private readonly CosmosClient _criticalClient;
    private readonly CosmosClient _analyticsClient;
    private readonly ILogger<CosmosDbBulkhead> _logger;
    
    // Container references for different workloads
    private Container _ordersContainer;
    private Container _analyticsContainer;
    
    public CosmosDbBulkhead(IConfiguration configuration, ILogger<CosmosDbBulkhead> logger)
    {
        _logger = logger;
        
        // Create separate clients with different configurations
        // Critical operations client - optimized for low latency
        _criticalClient = new CosmosClient(
            configuration["CosmosDb:ConnectionString"],
            new CosmosClientOptions
            {
                ApplicationName = "CriticalOperations",
                ConnectionMode = ConnectionMode.Direct, // For lower latency
                RequestTimeout = TimeSpan.FromSeconds(5),
                MaxRetryAttemptsOnRateLimitedRequests = 3,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(1)
            });
            
        // Analytics client - optimized for bulk operations with higher latency tolerance
        _analyticsClient = new CosmosClient(
            configuration["CosmosDb:ConnectionString"],
            new CosmosClientOptions
            {
                ApplicationName = "AnalyticsOperations",
                ConnectionMode = ConnectionMode.Gateway, // More suitable for analytics
                RequestTimeout = TimeSpan.FromSeconds(30),
                MaxRetryAttemptsOnRateLimitedRequests = 9,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(10)
            });
            
        InitializeContainers();
    }
    
    private void InitializeContainers()
    {
        // Get references to containers
        // These would be created with appropriate RU/s for their workload
        _ordersContainer = _criticalClient.GetContainer("ecommerce", "orders");
        _analyticsContainer = _analyticsClient.GetContainer("ecommerce", "analytics");
    }
    
    // Method for critical order operations
    public async Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _ordersContainer.CreateItemAsync(
                order, 
                new PartitionKey(order.CustomerId),
                cancellationToken: cancellationToken);
                
            _logger.LogInformation(
                "Order created. Request charge: {RequestCharge} RUs", 
                response.RequestCharge);
                
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "Rate limited while creating order. Retries exhausted. Request Charge: {RequestCharge}",
                ex.RequestCharge);
                
            throw new BulkheadRateLimitedException("Order creation rate limited", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            throw;
        }
    }
    
    // Method for non-critical analytics operations
    public async Task LogOrderAnalyticsAsync(OrderAnalytics analytics, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _analyticsContainer.CreateItemAsync(
                analytics, 
                new PartitionKey(analytics.Category),
                cancellationToken: cancellationToken);
                
            _logger.LogInformation(
                "Analytics logged. Request charge: {RequestCharge} RUs", 
                response.RequestCharge);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // For non-critical operations, we might just log the error instead of throwing
            _logger.LogWarning(
                "Rate limited while logging analytics. Request Charge: {RequestCharge}",
                ex.RequestCharge);
                
            // We could implement a fallback here, such as queuing the analytics for later processing
            await QueueAnalyticsForLaterProcessingAsync(analytics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging analytics");
            // Non-critical error, don't throw
        }
    }
    
    private Task QueueAnalyticsForLaterProcessingAsync(OrderAnalytics analytics)
    {
        // Implementation to queue analytics for later processing
        // This could use Azure Storage Queue, Service Bus, etc.
        return Task.CompletedTask;
    }
    
    public class BulkheadRateLimitedException : Exception
    {
        public BulkheadRateLimitedException(string message, Exception innerException) 
            : base(message, innerException) { }
    }
}
```

### Azure Service Bus

**Strategies**:
- Use separate queues or topics for different message categories
- Configure different prefetch counts for different message processors
- Implement separate processing hosts for critical vs. non-critical messages

**Implementation Example (C#)**:
```csharp
// Service Bus bulkhead implementation with separate client instances
public class ServiceBusBulkhead
{
    private readonly ServiceBusClient _criticalClient;
    private readonly ServiceBusClient _nonCriticalClient;
    private readonly ILogger<ServiceBusBulkhead> _logger;
    
    // Senders for different message categories
    private ServiceBusSender _ordersSender;
    private ServiceBusSender _notificationsSender;
    
    public ServiceBusBulkhead(IConfiguration configuration, ILogger<ServiceBusBulkhead> logger)
    {
        _logger = logger;
        
        // Create separate clients with different configurations
        // Critical operations client
        _criticalClient = new ServiceBusClient(
            configuration["ServiceBus:ConnectionString"],
            new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets,
                RetryOptions = new ServiceBusRetryOptions
                {
                    MaxRetries = 5,
                    Mode = ServiceBusRetryMode.Exponential,
                    MaxDelay = TimeSpan.FromSeconds(5)
                }
            });
            
        // Non-critical client
        _nonCriticalClient = new ServiceBusClient(
            configuration["ServiceBus:ConnectionString"],
            new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets,
                RetryOptions = new ServiceBusRetryOptions
                {
                    MaxRetries = 10,
                    Mode = ServiceBusRetryMode.Exponential,
                    MaxDelay = TimeSpan.FromSeconds(30)
                }
            });
            
        InitializeSenders();
    }
    
    private void InitializeSenders()
    {
        _ordersSender = _criticalClient.CreateSender("orders");
        _notificationsSender = _nonCriticalClient.CreateSender("notifications");
    }
    
    // Method for sending critical order messages
    public async Task SendOrderMessageAsync(OrderMessage orderMessage, CancellationToken cancellationToken)
    {
        try
        {
            // Create a message with critical settings
            var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(orderMessage))
            {
                MessageId = orderMessage.OrderId,
                ContentType = "application/json",
                Subject = "Order",
                TimeToLive = TimeSpan.FromHours(24), // Critical messages kept longer
                ScheduledEnqueueTime = DateTimeOffset.UtcNow
            };
            
            await _ordersSender.SendMessageAsync(message, cancellationToken);
            _logger.LogInformation("Order message sent: {OrderId}", orderMessage.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order message: {OrderId}", orderMessage.OrderId);
            throw; // Rethrow for critical messages
        }
    }
    
    // Method for sending non-critical notification messages
    public async Task SendNotificationMessageAsync(NotificationMessage notification, CancellationToken cancellationToken)
    {
        try
        {
            // Create a message with non-critical settings
            var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(notification))
            {
                MessageId = Guid.NewGuid().ToString(),
                ContentType = "application/json",
                Subject = "Notification",
                TimeToLive = TimeSpan.FromHours(2), // Non-critical messages kept for less time
                ScheduledEnqueueTime = DateTimeOffset.UtcNow
            };
            
            await _notificationsSender.SendMessageAsync(message, cancellationToken);
            _logger.LogInformation("Notification message sent: {Type}", notification.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification message: {Type}", notification.Type);
            // For non-critical messages, we might just log instead of rethrowing
        }
    }
    
    // Factory method for creating a message receiver with appropriate settings
    public ServiceBusProcessor CreateOrderProcessor(
        Func<ProcessMessageEventArgs, Task> messageHandler,
        Func<ProcessErrorEventArgs, Task> errorHandler)
    {
        return _criticalClient.CreateProcessor("orders", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 10, // Limit concurrency for critical processing
            PrefetchCount = 20,
            AutoCompleteMessages = false, // Manual completion for reliability
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
        });
    }
    
    // Factory method for creating a non-critical message receiver
    public ServiceBusProcessor CreateNotificationProcessor(
        Func<ProcessMessageEventArgs, Task> messageHandler,
        Func<ProcessErrorEventArgs, Task> errorHandler)
    {
        return _nonCriticalClient.CreateProcessor("notifications", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 30, // Higher concurrency for non-critical processing
            PrefetchCount = 100,
            AutoCompleteMessages = true, // Auto-completion for simplicity
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(2)
        });
    }
}
```

## Common Pitfalls

1. **Insufficient Isolation**: Not properly isolating different components or workloads
   - Solution: Use separate resource pools, connection pools, or processes

2. **Resource Starvation**: Setting bulkhead limits too low, causing resource starvation
   - Solution: Properly size resource pools based on expected workloads and importance

3. **Improper Fallbacks**: Not implementing fallbacks when bulkheads reject operations
   - Solution: Always provide fallback mechanisms for rejected operations

4. **Ignoring Failures**: Not properly handling and logging bulkhead rejections
   - Solution: Monitor and alert on bulkhead rejections as they indicate capacity issues

5. **Static Configuration**: Not adjusting bulkhead sizes based on load patterns
   - Solution: Implement dynamic bulkhead sizing or auto-scaling

## Next Steps

Continue to the [Fallback Pattern](06-fallback-pattern.md) to learn how to provide alternative functionality when a service fails.
