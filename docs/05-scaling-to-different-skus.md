# Scaling to Different SKUs Pattern

## Context and Problem

Cloud applications often need to adapt to varying load conditions, cost constraints, and performance requirements. Azure services are offered in different Service Tiers or Stock Keeping Units (SKUs) that provide different capabilities, performance levels, and cost structures.

The challenge is designing applications that can seamlessly scale across these different SKUs without major architectural changes or downtime.

## Solution

The "Scaling to Different SKUs" pattern involves designing applications that can dynamically or manually transition between different service tiers while maintaining functionality, though potentially with different performance characteristics or feature availability.

![Scaling to Different SKUs](../images/scaling-to-different-skus.png)

## Key Principles

1. **Service Abstraction**: Abstract the underlying Azure services behind interfaces that handle the differences between SKUs.

2. **Feature Detection**: Implement runtime detection of available features rather than hardcoding expectations.

3. **Graceful Degradation**: If a lower SKU lacks certain features, degrade gracefully rather than failing completely.

4. **Configuration-Driven Behavior**: Use configuration to modify application behavior based on the current SKU.

5. **Monitoring and Auto-scaling**: Implement robust monitoring to trigger scaling between SKUs based on predefined metrics.

## Implementation Guidelines

### 1. Database Services (Azure SQL, Cosmos DB)

- Design database access code to handle varying throughput limits and latency.
- Implement retry logic with appropriate backoff for throttling errors.
- For lower SKUs, consider implementing caching to reduce database load.
- Use database performance tiers appropriate for the workload.

```csharp
public async Task<Customer> GetCustomerAsync(string id)
{
    // Configuration-driven connection behavior
    var connectionString = _currentSku switch
    {
        "Basic" => _config.BasicSqlConnectionString,
        "Standard" => _config.StandardSqlConnectionString,
        "Premium" => _config.PremiumSqlConnectionString,
        _ => throw new InvalidOperationException($"Unknown SKU: {_currentSku}")
    };
    
    using var connection = new SqlConnection(connectionString);
    // Implement with appropriate timeouts for the SKU
    // ...
}
```

### 2. Compute Services (App Service, Functions)

- Design your application to function with different numbers of instances.
- Implement horizontal scaling for compute-intensive operations.
- Cache computation results when appropriate to reduce processing needs.
- Use App Service deployment slots for zero-downtime transitions between SKUs.

```csharp
public class ComputeService
{
    private readonly string _currentSku;
    
    // Adjust behavior based on SKU
    public async Task<Result> ProcessDataAsync(Data data)
    {
        if (_currentSku == "Basic")
        {
            // Simplified processing for Basic SKU
            return await SimpleProcessingAsync(data);
        }
        else
        {
            // Advanced processing for higher SKUs
            return await AdvancedProcessingAsync(data);
        }
    }
}
```

### 3. Storage Services (Blob Storage, File Storage)

- Design for different storage performance tiers.
- For lower SKUs, consider optimizing access patterns to reduce transaction costs.
- Implement appropriate retry policies for storage operations.

### 4. Messaging Services (Service Bus, Event Hubs)

- Design to handle different throughput limits.
- Implement buffering or throttling for lower SKUs to prevent message loss.
- Consider partitioning strategies for higher throughput in premium SKUs.

## Scaling Strategies

### Vertical Scaling (Scaling Up/Down)

- Change the SKU or tier of a service to one with more/fewer resources.
- Usually requires a brief downtime or redeployment.
- Appropriate for predictable, steady growth.

### Horizontal Scaling (Scaling Out/In)

- Add or remove instances of the same SKU.
- Generally can be done without downtime.
- Better for handling spiky, unpredictable loads.

### Combined Approach

The most effective strategy often combines both vertical and horizontal scaling:

1. Start with an appropriate SKU for baseline load.
2. Scale out/in to handle fluctuations around that baseline.
3. Scale up/down when baseline load significantly changes.

## Monitoring and Automation

- Implement comprehensive monitoring to track resource utilization.
- Set up alerts for when utilization approaches SKU limits.
- Use Azure Automation or Functions to implement auto-scaling logic.
- Consider using Azure Monitor and Application Insights for advanced metrics.

```csharp
// Example of a function that could be triggered to evaluate scaling needs
public async Task EvaluateScalingNeedsAsync(ILogger log)
{
    var metrics = await _monitoringService.GetCurrentMetricsAsync();
    
    if (metrics.CpuUtilization > 80 && _currentSku != "Premium")
    {
        log.LogInformation("High CPU utilization detected. Scaling up to Premium SKU.");
        await _scalingService.ScaleUpToSku("Premium");
    }
    else if (metrics.CpuUtilization < 20 && metrics.RequestCount < 1000 && _currentSku == "Premium")
    {
        log.LogInformation("Low utilization detected. Scaling down to Standard SKU.");
        await _scalingService.ScaleDownToSku("Standard");
    }
}
```

## Considerations for Specific Azure Services

### Azure App Service

- Use deployment slots for testing new SKUs before swapping.
- Consider Premium V3 SKUs for applications requiring more memory.
- Be aware of connection limits in different tiers.

### Azure SQL Database

- Use elastic pools for multiple databases with variable usage patterns.
- Consider serverless options for intermittent workloads.
- Implement query optimization for consistent performance across SKUs.

### Azure Cosmos DB

- Adjust Request Units (RUs) based on workload and budget constraints.
- Consider Autoscale provisioning for variable workloads.
- Be aware of feature differences between free, standard, and dedicated tiers.

### Azure Cache for Redis

- Consider Premium tier for persistence, clustering, and high availability.
- Be mindful of memory limits in each SKU.
- Design eviction policies appropriate for your cache usage.

## Tradeoffs

### Advantages

- Cost optimization by matching resources to actual needs.
- Better user experience by scaling up during high demand.
- Reduced operational overhead through automation.

### Challenges

- Increased complexity in application design and testing.
- Potential for temporary performance degradation during scaling operations.
- Need for comprehensive monitoring and automation.

## When to Use This Pattern

- When your application has variable load patterns (e.g., seasonal spikes).
- When cost optimization is a key concern.
- When you need to support both development/testing and production environments with different performance requirements.

## When Not to Use This Pattern

- For applications with consistent, predictable load.
- When the complexity of supporting multiple SKUs outweighs the cost benefits.
- For services where downtime during scaling is unacceptable and the service doesn't support seamless scaling.

## Related Patterns

- [Cache-Aside Pattern](01-cache-aside-pattern.md) - Can help mitigate performance differences between SKUs.
- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md) - Important for handling service degradation during scaling operations.
- [Bulkhead Pattern](05-bulkhead-pattern.md) - Can isolate failures that might occur during scaling.
- [Throttling Pattern](throttling-pattern.md) - Can help manage load within SKU limits.

## Azure Examples

1. **Azure App Service** - Choose between Free, Shared, Basic, Standard, Premium, PremiumV2, and PremiumV3 SKUs.
2. **Azure SQL Database** - Scale between Basic, Standard, and Premium tiers, or use Serverless.
3. **Azure Cosmos DB** - Adjust provisioned throughput (RUs) or use autoscale.
4. **Azure Cache for Redis** - Scale between Basic, Standard, and Premium tiers.
5. **Azure Service Bus** - Choose between Basic, Standard, and Premium SKUs.

## References

- [Azure App Service pricing tiers](https://azure.microsoft.com/en-us/pricing/details/app-service/windows/)
- [Azure SQL Database purchasing models](https://docs.microsoft.com/en-us/azure/azure-sql/database/purchasing-models)
- [Azure Cosmos DB capacity planner](https://docs.microsoft.com/en-us/azure/cosmos-db/estimate-ru-with-capacity-planner)
- [Azure Cache for Redis pricing](https://azure.microsoft.com/en-us/pricing/details/cache/)
