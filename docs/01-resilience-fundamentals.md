# Resilience Fundamentals for Azure Applications

## Core Concepts

Resilience in cloud applications refers to the ability to recover from failures and continue functioning. It's not just about avoiding failures, but expecting them and responding appropriately.

## Key Principles

### 1. Design for Failure

**Why**: In the cloud, failures are inevitable. Network issues, service outages, and resource exhaustion are common.

**Guidelines**:
- Assume any call to an external service can fail
- Design each component to handle the failure of its dependencies
- Use graceful degradation to maintain core functionality

**Anti-Pattern Example (C#)**:
```csharp
// Anti-pattern: No error handling
public async Task<string> GetUserDataAsync(string userId)
{
    var response = await _cosmosDbClient.ReadItemAsync<User>(userId, new PartitionKey(userId));
    return JsonSerializer.Serialize(response.Resource);
}
```

**Better Approach**:
```csharp
public async Task<string> GetUserDataAsync(string userId)
{
    try
    {
        var response = await _cosmosDbClient.ReadItemAsync<User>(userId, new PartitionKey(userId));
        return JsonSerializer.Serialize(response.Resource);
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning($"User {userId} not found");
        return "{}"; // Return empty object instead of failing
    }
    catch (CosmosException ex)
    {
        _logger.LogError(ex, $"Cosmos DB error for user {userId}");
        // Could use a cached value or fallback here
        throw; // Rethrow if we have no fallback strategy
    }
}
```

### 2. Isolate Critical Paths

**Why**: Not all parts of your application have equal importance. Isolating critical paths ensures core functionality remains available.

**Guidelines**:
- Identify the critical business operations
- Design non-critical operations to fail without affecting critical ones
- Use architectural patterns like bulkheads to isolate components

**Example (TypeScript)**:
```typescript
// Isolating critical paths with Promise.allSettled
async function loadDashboard(userId: string): Promise<Dashboard> {
  // Critical data - wait for this to load
  const userProfile = await userService.getUserProfile(userId);
  
  // Non-critical components - load in parallel, accept partial failures
  const [ordersResult, recommendationsResult, notificationsResult] = 
    await Promise.allSettled([
      orderService.getRecentOrders(userId),
      recommendationService.getPersonalizedRecommendations(userId),
      notificationService.getPendingNotifications(userId)
    ]);
    
  return {
    userProfile,
    orders: ordersResult.status === 'fulfilled' ? ordersResult.value : [],
    recommendations: recommendationsResult.status === 'fulfilled' ? recommendationsResult.value : [],
    notifications: notificationsResult.status === 'fulfilled' ? notificationsResult.value : []
  };
}
```

### 3. Understand Service Behaviors and Limits

**Why**: Different Azure services have different limits, behaviors, and resilience guarantees.

**Guidelines**:
- Learn the service-specific error codes and retry policies
- Understand service quotas and throttling behaviors
- Design with service SLAs in mind

**Example**: Different Azure services require different approaches:
- Cosmos DB: Handle 429 (TooManyRequests) with backoff retries
- Azure Storage: Use the built-in retry policies but customize for your needs
- Azure SQL: Implement connection resiliency for transient errors

### 4. Test for Resilience

**Why**: Resilience patterns need to be tested under realistic failure conditions.

**Guidelines**:
- Implement chaos engineering practices
- Use fault injection to simulate service failures
- Test with realistic load patterns

**Testing Example**:
```csharp
// Using Polly's Simulated Chaos for testing resilience
public static class ChaosPolicy
{
    public static IAsyncPolicy CreateChaosPolicy()
    {
        return Policy
            .InjectAsync(
                // 20% of calls will fail with a timeout
                asyncInject: (context, ct) => Task.FromResult(0.2 > random.NextDouble()),
                asyncBehavior: (context, ct) => throw new TimeoutException())
            .WrapAsync(Policy
                // 10% of calls will fail with HTTP 500
                .InjectAsync(
                    asyncInject: (context, ct) => Task.FromResult(0.1 > random.NextDouble()),
                    asyncBehavior: (context, ct) => throw new HttpRequestException("Simulated HTTP 500 error")));
    }
    
    private static Random random = new Random();
}
```

### 5. Instrument Everything

**Why**: Without proper instrumentation, it's impossible to understand failure modes and recovery effectiveness.

**Guidelines**:
- Log all failures, retries, and recovery attempts
- Implement health checks for all components
- Use structured logging and distributed tracing

**Instrumentation Example (C#)**:
```csharp
public async Task<Order> CreateOrderAsync(OrderRequest request)
{
    using var activity = _activitySource.StartActivity("CreateOrder");
    activity?.SetTag("orderId", request.OrderId);
    activity?.SetTag("customerId", request.CustomerId);
    
    try
    {
        var order = await _orderService.CreateAsync(request);
        
        activity?.SetTag("orderStatus", "created");
        _logger.LogInformation("Order {OrderId} created successfully", order.Id);
        
        return order;
    }
    catch (Exception ex)
    {
        activity?.SetTag("error", true);
        activity?.SetTag("errorType", ex.GetType().Name);
        activity?.SetTag("errorMessage", ex.Message);
        
        _logger.LogError(ex, "Failed to create order for customer {CustomerId}", request.CustomerId);
        throw;
    }
}
```

## Next Steps

Continue to the [Retry Pattern](02-retry-pattern.md) for more detailed implementation guidance on one of the most important resilience patterns.
