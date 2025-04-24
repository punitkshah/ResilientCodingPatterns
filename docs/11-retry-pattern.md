# Retry Pattern for Azure Applications

## What is the Retry Pattern?

The Retry Pattern involves automatically retrying an operation that has failed, with the expectation that the failure is transient and may succeed on subsequent attempts.

## Why is the Retry Pattern Important?

In cloud environments, transient failures are common due to:
- Network latency and temporary connectivity issues
- Service throttling during high demand
- Resource contention in multi-tenant environments
- Brief service outages or upgrades

Properly implemented retry logic significantly improves application reliability without requiring manual intervention.

## Key Considerations

### 1. Identify Retryable vs. Non-Retryable Failures

**Why**: Not all failures should be retried. Retrying non-retryable failures wastes resources and can delay providing feedback to users.

**Guidelines**:
- Retry transient failures (network issues, throttling, etc.)
- Don't retry permanent failures (authentication errors, invalid inputs, etc.)
- Learn service-specific error codes and categorize them

**Anti-Pattern Example (C#)**:
```csharp
// Anti-pattern: Blindly retrying all errors
public async Task<BlobClient> UploadDocumentAsync(string fileName, Stream content)
{
    int retryCount = 0;
    while (retryCount < 3)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(content, overwrite: true);
            return blobClient;
        }
        catch (Exception) // Catches ALL exceptions, even those that should not be retried
        {
            retryCount++;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
    throw new Exception($"Failed to upload document after {retryCount} attempts");
}
```

**Better Approach**:
```csharp
public async Task<BlobClient> UploadDocumentAsync(string fileName, Stream content)
{
    int retryCount = 0;
    const int MaxRetries = 3;

    while (retryCount < MaxRetries)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(content, overwrite: true);
            return blobClient;
        }
        catch (RequestFailedException ex) when (
            ex.Status == 429 || // Too many requests
            ex.Status == 500 || // Internal server error
            ex.Status == 503)   // Service unavailable
        {
            retryCount++;
            if (retryCount >= MaxRetries) throw;
            
            // Exponential backoff with jitter
            var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)) 
                + TimeSpan.FromMilliseconds(new Random().Next(0, 1000));
            _logger.LogWarning("Transient error uploading blob. Retry {Retry} after {Delay}ms. Error: {Message}", 
                retryCount, delay.TotalMilliseconds, ex.Message);
                
            await Task.Delay(delay);
        }
        catch (Exception ex)
        {
            // Non-retryable error or max retries exceeded
            _logger.LogError(ex, "Non-retryable error uploading blob {FileName}", fileName);
            throw;
        }
    }
    
    // This should never be reached due to the throw inside the loop, but compiler doesn't know that
    throw new Exception($"Failed to upload document after {MaxRetries} attempts");
}
```

### 2. Implement Appropriate Backoff Strategies

**Why**: Immediate retries can overwhelm services and exacerbate problems. Proper backoff strategies give services time to recover.

**Guidelines**:
- Use exponential backoff for retries (increasing the wait time between retries)
- Add randomization (jitter) to prevent retry storms
- Set appropriate maximum retry counts and timeout limits

**Example (TypeScript)**:
```typescript
async function queryDatabaseWithRetry<T>(queryFn: () => Promise<T>): Promise<T> {
  const maxRetries = 5;
  const baseDelayMs = 100;
  
  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      return await queryFn();
    } catch (error) {
      if (!isRetryableError(error) || attempt >= maxRetries - 1) {
        throw error;
      }
      
      // Exponential backoff with full jitter
      const capDelay = 10000; // 10 seconds max
      const exponentialDelay = Math.min(capDelay, baseDelayMs * Math.pow(2, attempt));
      const jitteredDelay = Math.floor(Math.random() * exponentialDelay);
      
      console.log(`Attempt ${attempt + 1} failed. Retrying in ${jitteredDelay}ms`);
      await new Promise(resolve => setTimeout(resolve, jitteredDelay));
    }
  }
  
  // TypeScript needs this even though it's unreachable
  throw new Error("Max retries exceeded");
}

function isRetryableError(error: any): boolean {
  if (error.code === 'ECONNRESET' || error.code === 'ETIMEDOUT') {
    return true;
  }
  
  if (error.response && (
    error.response.status === 429 || // Too Many Requests
    error.response.status >= 500 && error.response.status < 600)) {
    return true;
  }
  
  return false;
}
```

### 3. Use Specialized Retry Libraries

**Why**: Retry logic is complex and error-prone when implemented manually. Specialized libraries handle edge cases and offer advanced features.

**Guidelines**:
- In .NET, use the Polly library for retry policies
- In Azure SDK clients, configure the built-in retry policies
- For HTTP clients, use middleware or interceptors for consistent retry behavior

**Example Using Polly (C#)**:
```csharp
// Example using Polly for retry policy with Azure Cosmos DB
public CosmosClient CreateCosmosClientWithResilience()
{
    // Define a retry policy with exponential backoff
    var retryPolicy = Policy
        .Handle<CosmosException>(e => 
            e.StatusCode == HttpStatusCode.TooManyRequests || // 429
            e.StatusCode == HttpStatusCode.ServiceUnavailable) // 503
        .WaitAndRetryAsync(
            retryCount: 5,
            sleepDurationProvider: (attempt, exception, context) =>
            {
                // For 429 responses, respect the Retry-After header if present
                if (exception is CosmosException cosmosEx && 
                    cosmosEx.StatusCode == HttpStatusCode.TooManyRequests &&
                    cosmosEx.RetryAfter.HasValue)
                {
                    return cosmosEx.RetryAfter.Value;
                }
                
                // Default to exponential backoff with jitter
                var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                var jitteredDelay = baseDelay + TimeSpan.FromMilliseconds(new Random().Next(0, 1000));
                return jitteredDelay;
            },
            onRetryAsync: (outcome, timespan, retryAttempt, context) =>
            {
                _logger.LogWarning(
                    "Request to Cosmos DB failed with {Exception}. Retrying in {Delay}ms. Attempt {Attempt}",
                    outcome.Exception.Message, timespan.TotalMilliseconds, retryAttempt);
                return Task.CompletedTask;
            });
            
    // Create CosmosClient with custom retry options
    var cosmosClientOptions = new CosmosClientOptions
    {
        MaxRetryAttemptsOnRateLimitedRequests = 0, // We'll handle retries ourselves with Polly
        MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.Zero
    };
    
    var client = new CosmosClient(
        _configuration["CosmosDb:Endpoint"],
        _configuration["CosmosDb:Key"],
        cosmosClientOptions);
        
    return client;
}
```

**Example Using Axios Interceptors (TypeScript)**:
```typescript
import axios, { AxiosError, AxiosInstance } from 'axios';

// Create an axios instance with retry capability
function createApiClientWithRetry(): AxiosInstance {
  const client = axios.create({
    baseURL: process.env.API_BASE_URL,
    timeout: 10000
  });
  
  // Add a response interceptor for retries
  client.interceptors.response.use(
    response => response, // Pass successful responses through
    async (error: AxiosError) => {
      const config = error.config;
      
      // Add retry count to config if it doesn't exist
      if (!config || !config.headers) {
        return Promise.reject(error);
      }
      
      const retryCount = Number(config.headers['x-retry-count'] || 0);
      const maxRetries = 3;
      
      // Check if we should retry
      if (
        retryCount < maxRetries &&
        (error.code === 'ECONNABORTED' ||
          (error.response && (
            error.response.status === 429 ||
            error.response.status >= 500
          ))
        )
      ) {
        // Increase retry count
        config.headers['x-retry-count'] = String(retryCount + 1);
        
        // Calculate delay with exponential backoff and jitter
        const delay = Math.min(
          30000, // Max 30 seconds
          Math.pow(2, retryCount) * 1000 + Math.random() * 1000
        );
        
        console.log(`Retrying request (${retryCount + 1}/${maxRetries}) after ${delay}ms`);
        
        // Wait before retrying
        await new Promise(resolve => setTimeout(resolve, delay));
        
        // Retry the request
        return client(config);
      }
      
      // If we shouldn't retry, reject with the error
      return Promise.reject(error);
    }
  );
  
  return client;
}
```

### 4. Service-Specific Retry Recommendations

**Why**: Different Azure services have different retry behaviors and recommendations.

#### Azure Cosmos DB
- Handle 429 (TooManyRequests) with backoff determined by the RetryAfter header
- Scale throughput appropriately to minimize throttling
- Use point-reads over queries when possible

#### Azure Storage
- Use exponential backoff for 500-level errors
- Watch for 403 (Server Busy) errors
- Use conditional requests (ETag/If-Match) to optimize retries

#### Azure SQL Database
- Implement connection resilience with SqlRetryLogicOption
- Be mindful of transaction scope during retries
- Implement idempotent operations for safe retries

**Example for Azure SQL Database (C#)**:
```csharp
// Setting up SQL connection with retry logic
public SqlConnection CreateSqlConnectionWithRetry()
{
    var connectionString = _configuration.GetConnectionString("DefaultConnection");
    var connection = new SqlConnection(connectionString);
    
    // Setup retry logic
    var retryLogic = new SqlRetryLogicOption
    {
        NumberOfTries = 5,
        MinTimeInterval = TimeSpan.FromSeconds(1),
        MaxTimeInterval = TimeSpan.FromSeconds(30),
        DeltaTime = TimeSpan.FromSeconds(1),
        TransientErrors = new Collection<int> 
        { 
            // Default list includes:
            // 1204, 1205, 1222, 10928, 10929, 10053, 10054, 10060, 18456, 40197, 40501, 40613
            
            // Adding additional error codes specific to our app
            40143, // Azure SQL throttling
            40613, // Database unavailable
            49918  // Not enough resources to process request
        }
    };
    
    connection.RetryLogicProvider = SqlConfigurableRetryFactory.CreateFixedRetryProvider(retryLogic);
    return connection;
}
```

#### Azure Service Bus
- Use built-in retry policy but customize for sensitivity
- Handle QuotaExceededException specially
- Consider duplicate detection for retried messages

## Common Pitfalls

1. **Retry Storms**: When many clients retry simultaneously after a service issue, creating a cascade of failures.
   - Solution: Implement random jitter in backoff times.

2. **Non-Idempotent Operations**: Retrying operations that weren't designed to be called multiple times.
   - Solution: Design APIs to be idempotent, using request IDs or conditional operations.

3. **Retry Blindness**: Not knowing if retries are happening or their success rates.
   - Solution: Log all retry attempts with details for monitoring.

4. **Excessive Retries**: Continuing to retry long after recovery is likely.
   - Solution: Implement circuit breakers alongside retry logic.

## Next Steps

Continue to the [Circuit Breaker Pattern](03-circuit-breaker-pattern.md) to learn how to prevent repeated retries when a system is unhealthy.
