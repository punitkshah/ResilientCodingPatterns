# Circuit Breaker Pattern for Azure Applications

## What is the Circuit Breaker Pattern?

The Circuit Breaker Pattern prevents an application from repeatedly trying to execute an operation that's likely to fail. Like an electrical circuit breaker, it stops the flow of requests when a service is unhealthy, allowing the failing service time to recover.

## Why is the Circuit Breaker Pattern Important?

While retry patterns help handle transient failures, they can be counterproductive during prolonged outages:
- Continuous retries to failing systems waste resources
- Retries may slow recovery of the struggling service
- Users experience delays instead of quick fallbacks
- Cascading failures can spread across your system

Circuit breakers detect unhealthy systems and "fail fast," improving overall system resilience.

## Key Concepts

A circuit breaker has three states:
1. **Closed** - Requests flow normally; failures are counted
2. **Open** - Requests fail immediately without attempting the operation
3. **Half-Open** - After a timeout, allows a limited number of test requests to check if the system has recovered

## Implementation Guidelines

### 1. Define Failure Thresholds

**Why**: You need clear criteria for when to "trip" the circuit breaker.

**Guidelines**:
- Set threshold based on failure rate (e.g., 50% of requests failing)
- Consider both error count and time window (e.g., 10 failures in 30 seconds)
- Adjust sensitivity based on the criticality of the service

**C# Example**:
```csharp
// Example circuit breaker configuration using Polly
public IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy()
{
    return Policy
        .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
        .OrTransientHttpError()
        .CircuitBreakerAsync(
            // Break circuit after 5 consecutive failures
            exceptionsAllowedBeforeBreaking: 5,
            
            // Keep circuit broken for 30 seconds
            durationOfBreak: TimeSpan.FromSeconds(30),
            
            // Action to take when the circuit breaks
            onBreak: (result, breakDuration) =>
            {
                _logger.LogWarning(
                    "Circuit breaker opened for {Service}. Breaking for {BreakDuration}ms due to: {Result}",
                    "PaymentService", 
                    breakDuration.TotalMilliseconds,
                    result.Exception?.Message ?? result.Result?.ReasonPhrase);
            },
            
            // Action to take when the circuit resets
            onReset: () =>
            {
                _logger.LogInformation("Circuit breaker reset for {Service}", "PaymentService");
            },
            
            // Action to take when the circuit enters half-open state
            onHalfOpen: () =>
            {
                _logger.LogInformation("Circuit breaker half-open for {Service}", "PaymentService");
            });
}
```

### 2. Implement Advanced Circuit Breaker Behavior

**Why**: Basic circuit breakers can be too binary. Advanced implementations provide more nuanced protection.

**Guidelines**:
- Use percentage-based failure thresholds instead of absolute counts
- Implement different policies for different error types
- Consider implementing a "bulkhead" alongside the circuit breaker

**TypeScript Example**:
```typescript
import { BrokenCircuitError, CircuitBreaker, CircuitState } from 'opossum';

function createAdvancedCircuitBreaker(serviceName: string) {
  // Create a circuit breaker that tracks failure percentage
  const options = {
    errorThresholdPercentage: 50,    // Open circuit if 50% of requests fail
    resetTimeout: 30000,             // 30 seconds until half-open
    rollingCountTimeout: 60000,      // Consider failures in the last 60 seconds
    rollingCountBuckets: 10,         // Split the time window into 10 buckets
    capacity: 25,                    // Allow max 25 concurrent requests
    volumeThreshold: 10              // Don't trip until we've seen 10 requests
  };
  
  const breaker = new CircuitBreaker(async function(endpoint: string, data: any) {
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    
    if (!response.ok) {
      throw new Error(`Service returned ${response.status}: ${response.statusText}`);
    }
    
    return response.json();
  }, options);
  
  // Add listeners for circuit state changes
  breaker.on('open', () => {
    console.log(`Circuit ${serviceName} opened due to failures`);
    sendAlertToMonitoringSystem(`Circuit for ${serviceName} has opened`, 'warning');
  });
  
  breaker.on('halfOpen', () => {
    console.log(`Circuit ${serviceName} is testing the service with trial requests`);
  });
  
  breaker.on('close', () => {
    console.log(`Circuit ${serviceName} closed, service is operational`);
    sendAlertToMonitoringSystem(`Circuit for ${serviceName} has closed`, 'info');
  });
  
  breaker.on('fallback', (result) => {
    console.log(`Request to ${serviceName} was handled by fallback`);
  });
  
  // Add a fallback strategy when the circuit is open
  breaker.fallback((endpoint, data) => {
    return { 
      success: false, 
      fallback: true, 
      message: `Service ${serviceName} is currently unavailable. Please try again later.` 
    };
  });
  
  return breaker;
}

// Usage example
const paymentServiceBreaker = createAdvancedCircuitBreaker('PaymentService');

async function processPayment(paymentDetails) {
  try {
    return await paymentServiceBreaker.fire('/api/payments', paymentDetails);
  } catch (error) {
    if (error instanceof BrokenCircuitError) {
      // Handle specifically as a circuit-open situation
      console.error('Payment service circuit is open, using alternative payment method');
      return await fallbackPaymentMethod(paymentDetails);
    }
    throw error;
  }
}
```

### 3. Implement Service-Specific Circuit Breakers

**Why**: Different services have different failure characteristics and impact on your application.

**Guidelines**:
- Create separate circuit breaker instances for each service/endpoint
- Set thresholds based on service criticality
- Consider implementing different fallback strategies for each service

**C# Example with Multiple Service Breakers**:
```csharp
public class ResilienceFactory
{
    private readonly ILogger<ResilienceFactory> _logger;
    private readonly IConfiguration _configuration;
    
    public ResilienceFactory(ILogger<ResilienceFactory> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }
    
    public IAsyncPolicy<HttpResponseMessage> CreatePolicyForCriticalService()
    {
        // More lenient circuit breaker for critical services
        // We'll keep trying longer before breaking the circuit
        return Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .OrTransientHttpError()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 10,  // Higher threshold
                durationOfBreak: TimeSpan.FromSeconds(15), // Shorter break
                onBreak: LogBreak("CriticalService"),
                onReset: LogReset("CriticalService"),
                onHalfOpen: LogHalfOpen("CriticalService"));
    }
    
    public IAsyncPolicy<HttpResponseMessage> CreatePolicyForNonCriticalService()
    {
        // Strict circuit breaker for non-critical services
        // We'll break the circuit faster to preserve system resources
        return Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .OrTransientHttpError()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,   // Lower threshold
                durationOfBreak: TimeSpan.FromSeconds(60), // Longer break
                onBreak: LogBreak("NonCriticalService"),
                onReset: LogReset("NonCriticalService"),
                onHalfOpen: LogHalfOpen("NonCriticalService"));
    }
    
    // Helper methods for logging circuit state changes
    private Action<DelegateResult<HttpResponseMessage>, TimeSpan> LogBreak(string serviceName) =>
        (result, timespan) => _logger.LogWarning(
            "Circuit breaker for {ServiceName} opened for {TimeSpan}ms due to: {Reason}",
            serviceName,
            timespan.TotalMilliseconds,
            result.Exception?.Message ?? result.Result?.ReasonPhrase);
            
    private Action LogReset(string serviceName) =>
        () => _logger.LogInformation("Circuit breaker for {ServiceName} reset", serviceName);
        
    private Action LogHalfOpen(string serviceName) =>
        () => _logger.LogInformation("Circuit breaker for {ServiceName} half-open, testing service", serviceName);
}
```

### 4. Add Monitoring and Alerting for Circuit Breakers

**Why**: Circuit breakers are early indicators of system problems. Monitoring their state provides valuable insights.

**Guidelines**:
- Log all circuit state transitions (closed → open → half-open → closed)
- Implement alerts when circuits break
- Track circuit breaker metrics (failure rates, open duration, etc.)

**Monitoring Example with Application Insights**:
```csharp
public class CircuitBreakerMonitoring
{
    private readonly TelemetryClient _telemetryClient;
    
    public CircuitBreakerMonitoring(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }
    
    public IAsyncPolicy<HttpResponseMessage> InstrumentedCircuitBreakerAsync(
        string serviceName,
        IAsyncPolicy<HttpResponseMessage> circuitBreakerPolicy)
    {
        return Policy
            .WrapAsync(
                // Inner policy to track circuit breaker events
                Policy<HttpResponseMessage>
                    .HandleInner<BrokenCircuitException>()
                    .OrInner<IsolatedCircuitException>()
                    .OrResult(r => !r.IsSuccessStatusCode)
                    .AsAsyncPolicy()
                    .ExecuteAsync(async (context, ct, policyResult) =>
                    {
                        // Capture circuit state before each execution
                        if (context.ContainsKey("CircuitState"))
                        {
                            var circuitState = context["CircuitState"].ToString();
                            _telemetryClient.TrackEvent("CircuitBreakerExecution", 
                                new Dictionary<string, string>
                                {
                                    ["ServiceName"] = serviceName,
                                    ["CircuitState"] = circuitState
                                });
                        }
                        
                        return await policyResult;
                    }),
                    
                // Wrap the actual circuit breaker
                circuitBreakerPolicy
                    .WrapAsync(Policy
                        .Handle<Exception>()
                        .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                        .AsAsyncPolicy()
                        .ExecuteAsync(async (context, token) =>
                        {
                            // Capture dependency call with App Insights
                            using var operation = _telemetryClient.StartOperation<DependencyTelemetry>(
                                $"{serviceName} call");
                            
                            operation.Telemetry.Type = "HTTP";
                            
                            try
                            {
                                var result = await policyResult;
                                
                                operation.Telemetry.Success = result.IsSuccessStatusCode;
                                operation.Telemetry.ResultCode = ((int)result.StatusCode).ToString();
                                
                                return result;
                            }
                            catch (Exception ex)
                            {
                                operation.Telemetry.Success = false;
                                _telemetryClient.TrackException(ex);
                                throw;
                            }
                        }))
            );
    }
}
```

### 5. Integration with Retry Policies

**Why**: Circuit breakers and retry policies work best together.

**Guidelines**:
- Circuit breakers should wrap retry policies to prevent exhaustive retries
- Configure retry policies to be less aggressive when circuit is half-open
- Use lower thresholds for half-open state

**Combined Retry and Circuit Breaker Example (C#)**:
```csharp
public IAsyncPolicy<HttpResponseMessage> CreateResiliencePolicy()
{
    // Define the retry policy
    var retryPolicy = Policy
        .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
        .OrTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: (retryAttempt, response, context) => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) 
                + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
            onRetryAsync: (outcome, timespan, retryCount, context) =>
            {
                _logger.LogWarning("Retrying request after {RetryDelay}ms. Attempt: {RetryCount}", 
                    timespan.TotalMilliseconds, retryCount);
                return Task.CompletedTask;
            }
        );

    // Define the circuit breaker policy
    var circuitBreakerPolicy = Policy
        .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
        .OrTransientHttpError()
        .CircuitBreakerAsync(
            exceptionsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, state) => 
                _logger.LogWarning("Circuit tripped due to failures!"),
            onReset: () => 
                _logger.LogInformation("Circuit reset. Service recovered."),
            onHalfOpen: () => 
                _logger.LogInformation("Circuit in half-open state. Testing service...")
        );

    // Combine policies: first retry, then circuit breaker
    return Policy.WrapAsync(circuitBreakerPolicy, retryPolicy);
}

// Usage in HTTP client registration
public void ConfigureServices(IServiceCollection services)
{
    services.AddHttpClient("MyServiceClient", client =>
    {
        client.BaseAddress = new Uri("https://my-service-endpoint.com");
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddPolicyHandler(provider => 
    {
        var logger = provider.GetRequiredService<ILogger<Startup>>();
        return CreateResiliencePolicy(logger);
    });
}
```

## Common Pitfalls

1. **Too Sensitive**: Setting thresholds too low, causing unnecessary circuit breaks
   - Solution: Tune thresholds based on normal error rates and service behavior

2. **Improper Timeout Handling**: Not accounting for slow responses, only failures
   - Solution: Include timeout exceptions in the circuit breaker policy

3. **No Fallbacks**: Breaking the circuit without providing alternatives
   - Solution: Always implement fallback mechanisms when using circuit breakers

4. **Insufficient Testing**: Not testing the circuit breaker behavior
   - Solution: Use chaos testing to verify circuit breaker triggers and recovery

## Azure Service-Specific Considerations

### Cosmos DB
- Use circuit breakers with the 429 (throttling) responses
- Consider using the request charge (RU) information to anticipate throttling

### Azure Functions
- Implement circuit breakers for outbound calls from Functions
- Be careful with circuit breakers on consumption plans due to instance recycling

### Azure SQL
- Circuit breakers are particularly useful for connection failures
- Consider using read replicas as fallbacks

## Next Steps

Continue to the [Timeout & Cancellation Patterns](04-timeout-cancellation.md) to learn how to prevent indefinite waiting for unresponsive services.
