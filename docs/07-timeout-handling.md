# Timeout & Cancellation Patterns for Azure Applications

## Why Timeouts and Cancellation Matter

In distributed systems, services may become unresponsive without returning an error. Without proper timeout and cancellation handling:

1. **Resource Exhaustion**: Threads and connections remain blocked indefinitely
2. **Cascading Failures**: Slow services can cause backups throughout your system
3. **Poor User Experience**: Users face hanging requests and unresponsive UIs
4. **Hidden Issues**: Problems may go undetected until they become severe

Implementing proper timeout and cancellation patterns helps maintain system responsiveness and resilience even when dependent services are slow or unresponsive.

## Timeout Patterns

### 1. Setting Appropriate Timeouts

**Why**: Default timeouts are often too long or nonexistent. Custom timeouts should align with business requirements and expected performance.

**Guidelines**:
- Set explicit timeouts on all external calls
- Define timeouts based on user expectations and SLAs
- Apply different timeouts for different operations (e.g., shorter for interactive operations)
- Never use infinite timeouts in production systems

**Anti-Pattern Example (C#)**:
```csharp
// Anti-pattern: Using default timeout which might be too long
public async Task<OrderInfo> GetOrderAsync(string orderId)
{
    // HttpClient has a default timeout of 100 seconds!
    var response = await _httpClient.GetAsync($"/api/orders/{orderId}");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<OrderInfo>();
}
```

**Better Approach**:
```csharp
public async Task<OrderInfo> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
{
    // Set a specific timeout for this operation
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    
    // Combine with the passed cancellation token
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        timeoutCts.Token, cancellationToken);
    
    try
    {
        var response = await _httpClient.GetAsync(
            $"/api/orders/{orderId}", linkedCts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderInfo>(linkedCts.Token);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        // This is specifically a timeout, not a user cancellation
        _logger.LogWarning("Request for order {OrderId} timed out after 3 seconds", orderId);
        throw new TimeoutException($"Request for order {orderId} timed out");
    }
}
```

### 2. Implementing Layered Timeouts

**Why**: Different components in your system may need different timeout behaviors.

**Guidelines**:
- Implement timeouts at multiple levels (transport, application, user interface)
- Make lower-level timeouts shorter than higher-level timeouts
- Ensure graceful handling of timeouts at each level

**TypeScript Example**:
```typescript
// Layered timeout approach
async function fetchDashboardData(userId: string): Promise<Dashboard> {
  // UI-level timeout (longest)
  const uiTimeoutMs = 8000;
  const uiTimeoutPromise = new Promise<never>((_, reject) => {
    setTimeout(() => reject(new Error('Dashboard request timed out')), uiTimeoutMs);
  });
  
  try {
    // Race between the UI timeout and the actual request
    return await Promise.race([
      uiTimeoutPromise,
      fetchDataWithComponentTimeouts(userId)
    ]) as Dashboard;
  } catch (error) {
    // Handle UI-level timeout
    console.error('Dashboard timed out, showing cached data');
    return getCachedDashboard(userId);
  }
}

async function fetchDataWithComponentTimeouts(userId: string): Promise<Dashboard> {
  // Create component-specific timeouts
  const userDataPromise = fetchWithTimeout(
    `/api/users/${userId}`, 
    { timeout: 3000 }
  );
  
  const ordersPromise = fetchWithTimeout(
    `/api/orders?userId=${userId}`, 
    { timeout: 5000 }
  );
  
  const recommendationsPromise = fetchWithTimeout(
    `/api/recommendations?userId=${userId}`, 
    { timeout: 4000 }
  );
  
  // Run all requests in parallel with their own timeouts
  const results = await Promise.allSettled([
    userDataPromise,
    ordersPromise,
    recommendationsPromise
  ]);
  
  // Build dashboard with available data, using fallbacks when needed
  return {
    userData: results[0].status === 'fulfilled' ? results[0].value : getDefaultUserData(),
    orders: results[1].status === 'fulfilled' ? results[1].value : [],
    recommendations: results[2].status === 'fulfilled' ? results[2].value : getDefaultRecommendations()
  };
}

async function fetchWithTimeout(url: string, options: { timeout: number }): Promise<any> {
  const { timeout } = options;
  
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeout);
  
  try {
    const response = await fetch(url, {
      signal: controller.signal
    });
    
    if (!response.ok) {
      throw new Error(`HTTP error! Status: ${response.status}`);
    }
    
    return await response.json();
  } catch (error) {
    if (error.name === 'AbortError') {
      console.warn(`Request to ${url} timed out after ${timeout}ms`);
      throw new Error(`Request timed out after ${timeout}ms`);
    }
    throw error;
  } finally {
    clearTimeout(timeoutId);
  }
}
```

### 3. Service-Specific Timeout Recommendations

**Why**: Different Azure services have different response time characteristics and timeout patterns.

**Guidelines**:

#### Azure Cosmos DB
- Start with 1-2 second timeouts for point reads
- Use longer timeouts (5-10 seconds) for complex queries
- Implement client-side timeouts in addition to the SDK's timeouts

```csharp
// Cosmos DB with custom timeout
public async Task<ItemResponse<Document>> GetDocumentWithTimeout(string id, string partitionKey)
{
    var requestOptions = new ItemRequestOptions
    {
        // SDK-level timeout (applies to a single attempt)
        RequestTimeout = TimeSpan.FromSeconds(2)
    };
    
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    
    try
    {
        return await _container.ReadItemAsync<Document>(
            id, 
            new PartitionKey(partitionKey), 
            requestOptions, 
            timeoutCts.Token);
    }
    catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.RequestTimeout)
    {
        _logger.LogWarning("Cosmos DB request timeout: {Message}", ex.Message);
        throw new TimeoutException("Database request timed out", ex);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        _logger.LogWarning("Client-side timeout for Cosmos DB request");
        throw new TimeoutException("Database request exceeded client timeout limit");
    }
}
```

#### Azure Storage
- Use shorter timeouts for blob metadata operations (1-3 seconds)
- Use adaptive timeouts for blob data operations based on size
- Set server timeout values in addition to client timeouts

```csharp
// Azure Blob Storage with size-based timeout
public async Task<Stream> DownloadBlobWithAdaptiveTimeout(string containerName, string blobName)
{
    var container = _blobServiceClient.GetBlobContainerClient(containerName);
    var blob = container.GetBlobClient(blobName);
    
    // First get blob properties to determine size and set an appropriate timeout
    var properties = await blob.GetPropertiesAsync();
    
    // Calculate timeout based on expected download time
    // Base timeout of 5 seconds + 1 second per MB with a maximum of 60 seconds
    var contentLengthMB = properties.Value.ContentLength / (1024 * 1024);
    var timeoutSeconds = Math.Min(5 + contentLengthMB, 60);
    
    var downloadOptions = new BlobDownloadOptions
    {
        // Server-side timeout header
        NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds)
    };
    
    using var timeoutCts = new CancellationTokenSource(
        TimeSpan.FromSeconds(timeoutSeconds + 5)); // Client timeout slightly longer
    
    try
    {
        var response = await blob.DownloadStreamingAsync(
            downloadOptions, 
            timeoutCts.Token);
            
        return response.Value.Content;
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        _logger.LogWarning(
            "Download timed out for blob {BlobName} ({SizeMB}MB)",
            blobName, contentLengthMB);
            
        throw new TimeoutException($"Download timed out for blob {blobName}");
    }
}
```

#### Azure SQL Database
- Set command timeouts based on query complexity
- Keep connection timeouts short (15 seconds maximum)
- Add custom timeout logic for long-running transactions

```csharp
// Adaptive SQL command timeout
public async Task<DataTable> ExecuteQueryWithAdaptiveTimeout(string sql, IDictionary<string, object> parameters)
{
    // Analyze the SQL query complexity to determine an appropriate timeout
    int timeoutSeconds = DetermineCommandTimeout(sql);
    
    using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();
    
    using var command = new SqlCommand(sql, connection)
    {
        CommandTimeout = timeoutSeconds
    };
    
    // Add parameters
    foreach (var param in parameters)
    {
        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
    }
    
    using var timeoutCts = new CancellationTokenSource(
        TimeSpan.FromSeconds(timeoutSeconds + 5)); // Client timeout slightly longer
    
    try
    {
        var result = new DataTable();
        using var reader = await command.ExecuteReaderAsync(timeoutCts.Token);
        result.Load(reader);
        return result;
    }
    catch (SqlException ex) when (ex.Number == -2)
    {
        _logger.LogWarning("SQL timeout executing query: {Query}", sql.Substring(0, 100));
        throw new TimeoutException("Database query timed out", ex);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        _logger.LogWarning("Client-side timeout executing query: {Query}", sql.Substring(0, 100));
        throw new TimeoutException("Database query exceeded client timeout limit");
    }
}

// Helper method to determine command timeout based on query complexity
private int DetermineCommandTimeout(string sql)
{
    // Simple heuristic based on query type and complexity
    sql = sql.ToLowerInvariant();
    
    if (sql.Contains("join") && sql.Count(c => c == 'j') >= 3)
    {
        // Complex join query
        return 60;
    }
    else if (sql.Contains("count") || sql.Contains("sum") || sql.Contains("avg"))
    {
        // Aggregate query
        return 30;
    }
    else if (sql.StartsWith("insert") || sql.StartsWith("update") || sql.StartsWith("delete"))
    {
        // Data modification
        return 20;
    }
    else
    {
        // Simple select
        return 10;
    }
}
```

## Cancellation Patterns

### 1. Implementing Cooperative Cancellation

**Why**: Proper cancellation allows resources to be released promptly and operations to be gracefully aborted.

**Guidelines**:
- Pass CancellationToken through all async call chains
- Check cancellation status regularly in long-running operations
- Implement cancellation handlers to release resources

**C# Example**:
```csharp
// Implementing cooperative cancellation
public async Task<ReportData> GenerateReportAsync(ReportRequest request, CancellationToken cancellationToken)
{
    // Register a callback to log cancellation requests
    using var registration = cancellationToken.Register(() => 
        _logger.LogInformation("Report generation for {ReportId} was cancelled", request.ReportId));
    
    _logger.LogInformation("Starting report generation for {ReportId}", request.ReportId);
    
    // Check cancellation before expensive operations
    cancellationToken.ThrowIfCancellationRequested();
    
    // Fetch data from multiple sources, passing the cancellation token
    var customerData = await _customerService.GetCustomerDataAsync(
        request.CustomerId, cancellationToken);
    
    // Check cancellation between operations
    cancellationToken.ThrowIfCancellationRequested();
    
    var transactionData = await _transactionService.GetTransactionsAsync(
        request.CustomerId, 
        request.StartDate, 
        request.EndDate, 
        cancellationToken);
    
    // For CPU-bound work, periodically check cancellation
    var reportData = new ReportData
    {
        CustomerId = request.CustomerId,
        CustomerName = customerData.Name,
        ReportId = request.ReportId,
        GeneratedDate = DateTime.UtcNow,
        Transactions = new List<TransactionSummary>()
    };
    
    // Process transactions in chunks to allow cancellation
    const int chunkSize = 100;
    for (int i = 0; i < transactionData.Count; i += chunkSize)
    {
        // Check cancellation periodically during CPU-bound work
        if (i % chunkSize == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        
        var chunk = transactionData.Skip(i).Take(chunkSize);
        var summaries = ProcessTransactionChunk(chunk);
        reportData.Transactions.AddRange(summaries);
    }
    
    _logger.LogInformation("Completed report generation for {ReportId}", request.ReportId);
    return reportData;
}

private IEnumerable<TransactionSummary> ProcessTransactionChunk(IEnumerable<Transaction> transactions)
{
    // CPU-bound processing of transactions
    // ...
    return transactions.Select(t => new TransactionSummary { /* ... */ });
}
```

### 2. User-Initiated vs. System-Initiated Cancellation

**Why**: Different types of cancellation may require different handling strategies.

**Guidelines**:
- Distinguish between user cancellations and system timeouts
- Provide appropriate feedback for user cancellations
- Log and monitor system-initiated cancellations for health metrics

**TypeScript Example**:
```typescript
// Handling different types of cancellation
export async function processUpload(
  file: File, 
  options: { 
    onProgress: (percent: number) => void,
    onCancel: () => void
  }
): Promise<UploadResult> {
  // Create an AbortController for user cancellation
  const userAbortController = new AbortController();
  
  // Create a timeout AbortController for system timeout
  const timeoutAbortController = new AbortController();
  const timeoutId = setTimeout(() => timeoutAbortController.abort(), 30000);
  
  // Allow external code to cancel the upload
  const cancelUpload = () => {
    userAbortController.abort();
    options.onCancel();
  };
  
  try {
    // Get pre-signed URL for upload
    const uploadUrl = await getPresignedUrl(file.name, file.type);
    
    // Combine the abort signals
    const combinedSignal = combineAbortSignals(
      userAbortController.signal, 
      timeoutAbortController.signal
    );
    
    // Upload the file with progress tracking
    const response = await uploadWithProgress(
      uploadUrl, 
      file, 
      (percent) => options.onProgress(percent),
      combinedSignal
    );
    
    if (!response.ok) {
      throw new Error(`Upload failed: ${response.status} ${response.statusText}`);
    }
    
    return {
      success: true,
      fileId: await response.text()
    };
  } catch (error) {
    // Determine the type of cancellation
    if (userAbortController.signal.aborted) {
      console.log('Upload was cancelled by the user');
      return {
        success: false,
        cancelled: true,
        error: 'Upload cancelled by user'
      };
    } else if (timeoutAbortController.signal.aborted) {
      console.error('Upload timed out after 30 seconds');
      return {
        success: false,
        timedOut: true,
        error: 'Upload timed out'
      };
    } else {
      console.error('Upload failed with error:', error);
      return {
        success: false,
        error: error.message || 'Unknown error during upload'
      };
    }
  } finally {
    clearTimeout(timeoutId);
  }
  
  // Return the cancel function to allow external cancellation
  return { cancel: cancelUpload };
}

// Helper function to upload with progress
async function uploadWithProgress(
  url: string, 
  file: File, 
  onProgress: (percent: number) => void,
  signal: AbortSignal
): Promise<Response> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    
    // Set up progress handling
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        const percentComplete = Math.round((event.loaded / event.total) * 100);
        onProgress(percentComplete);
      }
    };
    
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(new Response(xhr.responseText, {
          status: xhr.status,
          statusText: xhr.statusText
        }));
      } else {
        reject(new Error(`HTTP Error: ${xhr.status}`));
      }
    };
    
    xhr.onerror = () => reject(new Error('Network error during upload'));
    xhr.ontimeout = () => reject(new Error('Upload timed out'));
    
    // Handle abort signal
    if (signal) {
      if (signal.aborted) {
        reject(new DOMException('The operation was aborted', 'AbortError'));
        return;
      }
      
      signal.addEventListener('abort', () => {
        xhr.abort();
        reject(new DOMException('The operation was aborted', 'AbortError'));
      });
    }
    
    xhr.open('PUT', url);
    xhr.send(file);
  });
}

// Helper to combine multiple abort signals
function combineAbortSignals(...signals: AbortSignal[]): AbortSignal {
  const controller = new AbortController();
  
  for (const signal of signals) {
    if (signal.aborted) {
      controller.abort();
      break;
    }
    
    signal.addEventListener('abort', () => controller.abort());
  }
  
  return controller.signal;
}
```

### 3. Resource Cleanup on Cancellation

**Why**: Proper resource cleanup prevents leaks and ensures system stability.

**Guidelines**:
- Use try/finally blocks to guarantee cleanup
- Implement IDisposable/AutoCloseable patterns
- Close connections and release locks explicitly

**C# Example**:
```csharp
// Proper resource cleanup on cancellation
public async Task ProcessBatchAsync(IEnumerable<string> items, CancellationToken cancellationToken)
{
    // Acquire resources
    SemaphoreSlim semaphore = new SemaphoreSlim(10); // Limit concurrent processing
    List<Task> tasks = new List<Task>();
    
    try
    {
        foreach (var item in items)
        {
            // Respect cancellation before creating new tasks
            cancellationToken.ThrowIfCancellationRequested();
            
            await semaphore.WaitAsync(cancellationToken);
            
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessItemAsync(item, cancellationToken);
                }
                finally
                {
                    // Always release the semaphore, even on cancellation
                    semaphore.Release();
                }
            }, cancellationToken));
        }
        
        // Wait for all tasks to complete
        await Task.WhenAll(tasks);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Batch processing was cancelled");
        
        // Wait for any in-progress tasks to complete or cancel
        var completedTasks = await Task.WhenAll(
            tasks.Select(t => t.ContinueWith(task => task.Status))
        );
        
        _logger.LogInformation(
            "Task statuses after cancellation: Completed={Completed}, Canceled={Canceled}, Faulted={Faulted}",
            completedTasks.Count(s => s == TaskStatus.RanToCompletion),
            completedTasks.Count(s => s == TaskStatus.Canceled),
            completedTasks.Count(s => s == TaskStatus.Faulted));
            
        throw; // Re-throw the cancellation
    }
    finally
    {
        // Dispose of resources
        semaphore.Dispose();
    }
}

private async Task ProcessItemAsync(string item, CancellationToken cancellationToken)
{
    // Acquire additional resources
    using var client = new HttpClient();
    await using var fileStream = new FileStream($"output/{item}.txt", FileMode.Create);
    
    try
    {
        var response = await client.GetAsync(
            $"https://api.example.com/items/{item}", 
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
            
        response.EnsureSuccessStatusCode();
        
        // Stream the response to the file
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await contentStream.CopyToAsync(fileStream, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Processing cancelled for item {Item}", item);
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing item {Item}", item);
        throw;
    }
}
```

## Common Pitfalls

1. **Orphaned Resources**: Failing to release resources when operations are cancelled
   - Solution: Use try/finally blocks and dispose patterns

2. **Cascading Timeouts**: Setting uniform timeouts that don't account for the call chain
   - Solution: Adjust timeouts at each level to account for the full call chain

3. **Timeout Confusion**: Confusing client timeouts, server timeouts, and retry timeouts
   - Solution: Clearly document and distinguish between different timeout types

4. **Missing Propagation**: Not propagating cancellation tokens through the call stack
   - Solution: Pass cancellation tokens to all async methods, even those that don't directly use them

5. **Ineffective Cancellation**: Checking cancellation too infrequently in long-running operations
   - Solution: Add regular cancellation checks in loops and long-running operations

## Service-Specific Best Practices

### Azure Functions
- Set function timeout lower than the platform timeout
- Use durable functions for long-running operations with checkpoints
- Implement host.json timeout settings for the entire function app

### Azure Service Bus / Event Hub Processing
- Configure consumer client timeouts appropriately
- Implement lease management with reasonable timeouts
- Handle cancellation gracefully to avoid message loss

### Azure Key Vault
- Set short timeouts for critical security operations
- Use timeouts that align with user-facing operations
- Implement fallbacks for timeout scenarios

## Next Steps

Continue to the [Bulkhead Pattern](05-bulkhead-pattern.md) to learn how to isolate failures in your application through resource partitioning.
