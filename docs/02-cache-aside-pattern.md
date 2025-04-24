# Cache-Aside Pattern for Azure Applications

## Overview

The Cache-Aside pattern (also known as Lazy Loading) improves application performance and scalability by loading data on-demand into a cache from a data store. This pattern helps reduce latency, minimize load on the underlying data store, and optimize resource utilization.

## Problem Statement

Applications frequently access the same data multiple times. Repeatedly retrieving this data from the original data store introduces:
- Unnecessary latency in application responses
- Excessive load on backend databases
- Increased costs due to database transactions
- Potential for throttling during high-demand periods

## Solution: Cache-Aside Pattern

With the Cache-Aside pattern:
1. When the application needs data, it first checks the cache
2. If the data is found in the cache (a cache hit), it's returned immediately
3. If the data is not found (a cache miss), the application retrieves it from the data store, adds it to the cache, and then returns it

This "lazy loading" approach ensures that only data that's actually needed gets cached, optimizing cache memory usage.

## Benefits

- **Improved Performance**: Accessing cached data is typically faster than retrieving from a database
- **Reduced Database Load**: Fewer queries reach the underlying data store
- **Resilience to Backend Failures**: Applications can still serve cached data when the backend is unavailable
- **Cost Reduction**: Lower database transaction counts can reduce costs in pay-per-use services
- **Improved Scalability**: A cache can handle more concurrent requests than most databases

## Implementation Considerations

### 1. Cache Expiration Strategy

**Why**: Data in the source system changes over time, making cached data stale.

**Guidelines**:
- Set appropriate Time-to-Live (TTL) values based on how frequently data changes
- Consider using a combination of TTL and explicit invalidation for frequently updated data
- For relatively static data, use longer cache durations

### 2. Cache Concurrency

**Why**: Multiple instances of an application might try to update the cache simultaneously.

**Guidelines**:
- Use atomic cache operations when available
- Consider implementing cache semaphores or distributed locks for critical updates
- Use optimistic concurrency with versioning when appropriate

### 3. Cache Consistency

**Why**: Cached data can become inconsistent with the source data.

**Guidelines**:
- Implement cache invalidation when source data changes
- Consider event-based cache invalidation using message queues or event subscriptions
- For eventual consistency, implement background refresh mechanisms

### 4. Cache Failure Handling

**Why**: Cache services can fail or become unavailable.

**Guidelines**:
- Implement circuit breakers for cache operations
- Fall back to the data store if the cache is unavailable
- Log cache failures but don't let them cause application failures
- Consider local, in-memory caches as fallbacks for distributed caches

## Code Example: Cache-Aside in C#

```csharp
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text.Json;
using System.Threading.Tasks;

// Service that implements the Cache-Aside pattern
public class ProductService
{
    private readonly IDistributedCache _cache;
    private readonly IProductRepository _repository;
    private readonly ILogger<ProductService> _logger;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);

    public ProductService(
        IDistributedCache cache,
        IProductRepository repository,
        ILogger<ProductService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Product> GetProductByIdAsync(string productId)
    {
        // Step 1: Try to get the item from the cache
        string cacheKey = $"product:{productId}";
        Product product = null;
        
        try
        {
            string cachedProduct = await _cache.GetStringAsync(cacheKey);
            
            if (!string.IsNullOrEmpty(cachedProduct))
            {
                _logger.LogInformation("Cache hit for product {ProductId}", productId);
                product = JsonSerializer.Deserialize<Product>(cachedProduct);
                return product;
            }
            
            _logger.LogInformation("Cache miss for product {ProductId}", productId);
        }
        catch (Exception ex)
        {
            // Log cache error but continue to fetch from repository
            _logger.LogWarning(ex, "Error accessing cache for product {ProductId}", productId);
        }
        
        // Step 2: On cache miss, get from the database
        product = await _repository.GetProductByIdAsync(productId);
        
        if (product == null)
        {
            _logger.LogInformation("Product {ProductId} not found in repository", productId);
            return null;
        }
        
        // Step 3: Add to cache
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration
            };
            
            string serializedProduct = JsonSerializer.Serialize(product);
            await _cache.SetStringAsync(cacheKey, serializedProduct, options);
            _logger.LogInformation("Added product {ProductId} to cache", productId);
        }
        catch (Exception ex)
        {
            // Log cache error but return the product anyway
            _logger.LogWarning(ex, "Failed to cache product {ProductId}", productId);
        }
        
        return product;
    }

    public async Task UpdateProductAsync(Product product)
    {
        // Update in repository
        await _repository.UpdateProductAsync(product);
        
        // Invalidate cache
        string cacheKey = $"product:{product.Id}";
        try
        {
            await _cache.RemoveAsync(cacheKey);
            _logger.LogInformation("Invalidated cache for product {ProductId}", product.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache for product {ProductId}", product.Id);
        }
    }
}

// Product entity
public class Product
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public DateTime LastUpdated { get; set; }
}

// Repository interface
public interface IProductRepository
{
    Task<Product> GetProductByIdAsync(string productId);
    Task UpdateProductAsync(Product product);
}
```

## Code Example: Cache-Aside in TypeScript

```typescript
import { Redis } from 'ioredis';
import { Logger } from './logger';

// Product type
interface Product {
  id: string;
  name: string;
  description: string;
  price: number;
  stockQuantity: number;
  lastUpdated: Date;
}

// Repository interface
interface ProductRepository {
  getProductById(productId: string): Promise<Product | null>;
  updateProduct(product: Product): Promise<void>;
}

// Cache-Aside implementation
export class ProductService {
  private readonly cacheDurationSeconds = 600; // 10 minutes
  
  constructor(
    private readonly cache: Redis,
    private readonly repository: ProductRepository,
    private readonly logger: Logger
  ) {}
  
  async getProductById(productId: string): Promise<Product | null> {
    // Step 1: Try to get the item from the cache
    const cacheKey = `product:${productId}`;
    
    try {
      // Try to get from cache
      const cachedProduct = await this.cache.get(cacheKey);
      
      if (cachedProduct) {
        this.logger.info(`Cache hit for product ${productId}`);
        return JSON.parse(cachedProduct);
      }
      
      this.logger.info(`Cache miss for product ${productId}`);
    } catch (error) {
      // Log cache error but continue to fetch from repository
      this.logger.warn(`Error accessing cache for product ${productId}: ${error.message}`);
    }
    
    // Step 2: On cache miss, get from the database
    const product = await this.repository.getProductById(productId);
    
    if (!product) {
      this.logger.info(`Product ${productId} not found in repository`);
      return null;
    }
    
    // Step 3: Add to cache
    try {
      await this.cache.set(
        cacheKey,
        JSON.stringify(product),
        'EX',
        this.cacheDurationSeconds
      );
      this.logger.info(`Added product ${productId} to cache`);
    } catch (error) {
      // Log cache error but return the product anyway
      this.logger.warn(`Failed to cache product ${productId}: ${error.message}`);
    }
    
    return product;
  }
  
  async updateProduct(product: Product): Promise<void> {
    // Update in repository
    await this.repository.updateProduct(product);
    
    // Invalidate cache
    const cacheKey = `product:${product.id}`;
    try {
      await this.cache.del(cacheKey);
      this.logger.info(`Invalidated cache for product ${product.id}`);
    } catch (error) {
      this.logger.warn(`Failed to invalidate cache for product ${product.id}: ${error.message}`);
    }
  }
}

// Example usage
async function main() {
  const redis = new Redis();
  const repository: ProductRepository = new DatabaseProductRepository();
  const logger = new ConsoleLogger();
  
  const productService = new ProductService(redis, repository, logger);
  
  // First call - will be a cache miss, fetched from database
  console.log('Fetching product first time:');
  const product1 = await productService.getProductById('prod-123');
  console.log(product1);
  
  // Second call - will be a cache hit
  console.log('\nFetching product second time:');
  const product2 = await productService.getProductById('prod-123');
  console.log(product2);
  
  // Update product - will invalidate cache
  console.log('\nUpdating product:');
  if (product2) {
    product2.price = 129.99;
    await productService.updateProduct(product2);
  }
  
  // After update - will be a cache miss again
  console.log('\nFetching product after update:');
  const product3 = await productService.getProductById('prod-123');
  console.log(product3);
}

main().catch(console.error);
```

## Azure-Specific Implementation

### Azure Redis Cache

Azure Redis Cache is a fully managed Redis cache service that provides high performance, low latency access to data. It's an ideal choice for implementing the Cache-Aside pattern in Azure applications.

**Key Configuration Options**:
- Choose the appropriate pricing tier based on your performance and feature requirements
- Enable non-TLS port for enhanced performance (though TLS is recommended for security)
- Configure data persistence if you need to preserve cache data across restarts
- Set appropriate eviction policy based on your application needs

**C# Example with Azure Redis Cache**:

```csharp
// Configure Redis cache in startup.cs
public void ConfigureServices(IServiceCollection services)
{
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = Configuration.GetConnectionString("AzureRedisCache");
        options.InstanceName = "SampleInstance";
    });
    
    // Register other services
    services.AddScoped<IProductRepository, ProductRepository>();
    services.AddScoped<ProductService>();
}
```

### Azure Cosmos DB Time-to-Live (TTL)

Azure Cosmos DB offers TTL capabilities that can be used in conjunction with the Cache-Aside pattern for more sophisticated caching scenarios.

**Best Practices**:
- Use container-level TTL for global expiration settings
- Use item-level TTL for more granular control
- Consider using the Cosmos DB change feed to invalidate other caches when data changes

## When to Use

- For frequently read, rarely updated data
- When database access is expensive (in terms of latency or cost)
- For data that's expensive to compute or process
- In read-heavy applications where the same data is accessed repeatedly
- To improve resilience against backend service failures

## When Not to Use

- For data that changes frequently
- When consistency is more important than performance
- When the overhead of cache management exceeds the benefits
- For simple applications with low traffic

## Related Patterns

- [Retry Pattern](02-retry-pattern.md)
- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Bulkhead Pattern](05-bulkhead-pattern.md)
- [Sharding Pattern](12-sharding-pattern.md)

## Next Steps

Continue to the [Strangler Fig Pattern](03-strangler-fig-pattern.md) to learn how to incrementally migrate a legacy system to a new architecture.
