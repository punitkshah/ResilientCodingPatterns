# Sharding Pattern

## Overview

The Sharding Pattern involves dividing a large database into smaller, more manageable pieces called shards, which are distributed across multiple servers or database instances. Each shard contains a subset of the data, enabling horizontal scaling and improved fault isolation.

## Problem Statement

Large-scale applications often face challenges with database performance, scalability, and reliability. As data volumes grow, a single database instance becomes a bottleneck and a single point of failure. Additionally, cloud-based applications may need to distribute data geographically to reduce latency.

## Solution: Sharding Pattern

Implement database sharding to distribute data across multiple database instances:

1. **Choose a sharding key**: Select a data attribute to determine which shard should contain each record
2. **Design a sharding strategy**: Implement range-based, hash-based, or directory-based sharding
3. **Manage shard distribution**: Balance data across shards to avoid hotspots
4. **Handle cross-shard operations**: Implement strategies for queries that span multiple shards
5. **Plan for resharding**: Design systems to support adding or removing shards as scale changes

## Benefits

- Improves scalability by distributing load across multiple database instances
- Enhances availability since failures are isolated to individual shards
- Increases performance for queries that can target specific shards
- Enables geo-distribution of data to reduce latency for global applications
- Provides better resource utilization and cost efficiency

## Implementation Considerations

- Choose appropriate sharding keys to minimize cross-shard operations
- Implement a strategy for maintaining referential integrity across shards
- Design an approach for performing distributed transactions if needed
- Develop a monitoring system to detect shard imbalances
- Create a strategy for resharding without downtime

## Code Example: Sharding in C#

```csharp
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ResilientDataPatterns
{
    // Interface for a database shard
    public interface IShard<TEntity> where TEntity : class
    {
        Task<TEntity> GetByIdAsync(string id);
        Task InsertAsync(TEntity entity);
        Task UpdateAsync(TEntity entity);
        Task DeleteAsync(string id);
        Task<IEnumerable<TEntity>> QueryAsync(Func<TEntity, bool> predicate);
        string ShardId { get; }
    }

    // Example entity with a sharding key
    public class Customer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Region { get; set; } // This will be our sharding key
        public DateTime CreatedAt { get; set; }
        
        // Additional properties...
    }

    // Shard manager using geographical sharding strategy
    public class GeoShardManager<TEntity> where TEntity : class
    {
        private readonly Dictionary<string, IShard<TEntity>> _shards;
        private readonly Func<TEntity, string> _shardKeySelector;
        private readonly ILogger<GeoShardManager<TEntity>> _logger;

        public GeoShardManager(
            IEnumerable<IShard<TEntity>> shards, 
            Func<TEntity, string> shardKeySelector,
            ILogger<GeoShardManager<TEntity>> logger)
        {
            _shards = new Dictionary<string, IShard<TEntity>>();
            foreach (var shard in shards)
            {
                _shards[shard.ShardId] = shard;
            }
            
            _shardKeySelector = shardKeySelector ?? 
                throw new ArgumentNullException(nameof(shardKeySelector));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Get the appropriate shard for an entity
        public IShard<TEntity> GetShardForEntity(TEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            string shardKey = _shardKeySelector(entity);
            return GetShardByKey(shardKey);
        }

        // Get shard by sharding key
        public IShard<TEntity> GetShardByKey(string shardKey)
        {
            if (string.IsNullOrEmpty(shardKey))
            {
                throw new ArgumentException("Shard key cannot be null or empty", nameof(shardKey));
            }

            // In this example, we're using direct mapping where the shard key 
            // (region) maps directly to a shard ID
            if (_shards.TryGetValue(shardKey, out var shard))
            {
                return shard;
            }

            // If no direct mapping, you could implement a fallback strategy:
            // 1. Hash the key to map to a shard
            // 2. Use a default shard
            // 3. Throw an exception

            _logger.LogWarning("No shard found for key {ShardKey}, using default shard", shardKey);
            return _shards.Values.First(); // Default to first shard as fallback
        }

        // Get all shards for multi-shard operations
        public IEnumerable<IShard<TEntity>> GetAllShards()
        {
            return _shards.Values;
        }

        // Insert entity to the appropriate shard
        public async Task InsertAsync(TEntity entity)
        {
            var shard = GetShardForEntity(entity);
            _logger.LogInformation("Inserting entity to shard {ShardId}", shard.ShardId);
            await shard.InsertAsync(entity);
        }

        // Retrieve entity by ID - must check all shards if shard key is unknown
        public async Task<TEntity> GetByIdAsync(string id, string shardKey = null)
        {
            if (!string.IsNullOrEmpty(shardKey))
            {
                // If we know the shard key, we can go directly to the right shard
                var shard = GetShardByKey(shardKey);
                return await shard.GetByIdAsync(id);
            }
            
            // Otherwise, we need to search all shards (fan-out query)
            _logger.LogWarning("Performing fan-out query across all shards for ID {Id}", id);
            
            foreach (var shard in _shards.Values)
            {
                var result = await shard.GetByIdAsync(id);
                if (result != null)
                {
                    return result;
                }
            }
            
            return null;
        }

        // Execute a cross-shard query
        public async Task<IEnumerable<TEntity>> QueryAsync(Func<TEntity, bool> predicate)
        {
            _logger.LogInformation("Executing cross-shard query");
            
            var tasks = new List<Task<IEnumerable<TEntity>>>();
            foreach (var shard in _shards.Values)
            {
                tasks.Add(shard.QueryAsync(predicate));
            }
            
            var results = await Task.WhenAll(tasks);
            return results.SelectMany(r => r);
        }
    }

    // Example implementation of a customer service using sharding
    public class CustomerService
    {
        private readonly GeoShardManager<Customer> _shardManager;
        private readonly ILogger<CustomerService> _logger;

        public CustomerService(
            GeoShardManager<Customer> shardManager,
            ILogger<CustomerService> logger)
        {
            _shardManager = shardManager;
            _logger = logger;
        }

        public async Task<Customer> GetCustomerAsync(string customerId, string region = null)
        {
            try
            {
                return await _shardManager.GetByIdAsync(customerId, region);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customer {CustomerId}", customerId);
                throw;
            }
        }

        public async Task CreateCustomerAsync(Customer customer)
        {
            if (string.IsNullOrEmpty(customer.Region))
            {
                throw new ArgumentException("Customer region (shard key) cannot be null or empty");
            }

            try
            {
                customer.CreatedAt = DateTime.UtcNow;
                await _shardManager.InsertAsync(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating customer {CustomerId} in region {Region}", 
                    customer.Id, customer.Region);
                throw;
            }
        }

        public async Task<IEnumerable<Customer>> FindCustomersByNameAsync(string namePattern)
        {
            try
            {
                // This will execute across all shards
                return await _shardManager.QueryAsync(c => 
                    c.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for customers with name pattern {NamePattern}", 
                    namePattern);
                throw;
            }
        }
    }

    // Example of setting up the sharding infrastructure
    public class ShardingSetup
    {
        public static GeoShardManager<Customer> ConfigureCustomerSharding(ILoggerFactory loggerFactory)
        {
            var shards = new List<IShard<Customer>>
            {
                new CustomerShard("us-east", "Server=db1.example.com;Database=Customers_UsEast", 
                    loggerFactory.CreateLogger<CustomerShard>()),
                new CustomerShard("us-west", "Server=db2.example.com;Database=Customers_UsWest", 
                    loggerFactory.CreateLogger<CustomerShard>()),
                new CustomerShard("eu-west", "Server=db3.example.com;Database=Customers_EuWest", 
                    loggerFactory.CreateLogger<CustomerShard>()),
                new CustomerShard("asia-east", "Server=db4.example.com;Database=Customers_AsiaEast", 
                    loggerFactory.CreateLogger<CustomerShard>())
            };
            
            return new GeoShardManager<Customer>(
                shards,
                customer => customer.Region,
                loggerFactory.CreateLogger<GeoShardManager<Customer>>()
            );
        }
    }

    // Example implementation of a customer shard
    // In a real application, this would connect to an actual database
    public class CustomerShard : IShard<Customer>
    {
        private readonly string _shardId;
        private readonly string _connectionString;
        private readonly ILogger<CustomerShard> _logger;
        private readonly Dictionary<string, Customer> _customers = new Dictionary<string, Customer>();

        public CustomerShard(string shardId, string connectionString, ILogger<CustomerShard> logger)
        {
            _shardId = shardId;
            _connectionString = connectionString;
            _logger = logger;
        }

        public string ShardId => _shardId;

        public Task<Customer> GetByIdAsync(string id)
        {
            _logger.LogInformation("Getting customer {CustomerId} from shard {ShardId}", id, _shardId);
            
            if (_customers.TryGetValue(id, out var customer))
            {
                return Task.FromResult(customer);
            }
            
            return Task.FromResult<Customer>(null);
        }

        public Task InsertAsync(Customer entity)
        {
            _logger.LogInformation("Inserting customer {CustomerId} to shard {ShardId}", entity.Id, _shardId);
            _customers[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Customer entity)
        {
            if (!_customers.ContainsKey(entity.Id))
            {
                throw new KeyNotFoundException($"Customer {entity.Id} not found in shard {_shardId}");
            }
            
            _customers[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            if (!_customers.ContainsKey(id))
            {
                throw new KeyNotFoundException($"Customer {id} not found in shard {_shardId}");
            }
            
            _customers.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<Customer>> QueryAsync(Func<Customer, bool> predicate)
        {
            var results = _customers.Values.Where(predicate).ToList();
            return Task.FromResult<IEnumerable<Customer>>(results);
        }
    }
}
```

## Code Example: Sharding in TypeScript

```typescript
import { v4 as uuidv4 } from 'uuid';

// Interface for a shard
interface Shard<T> {
  getId(): string;
  insert(entity: T): Promise<void>;
  getById(id: string): Promise<T | null>;
  update(entity: T): Promise<void>;
  delete(id: string): Promise<void>;
  query(predicate: (entity: T) => boolean): Promise<T[]>;
}

// Entity interface with ID and shard key
interface ShardedEntity {
  id: string;
  [key: string]: any; // For other properties
}

// Example entity
interface Product extends ShardedEntity {
  id: string;
  name: string;
  category: string; // This will be our shard key
  price: number;
  stock: number;
  createdAt: Date;
}

// Shard implementation
class ProductShard implements Shard<Product> {
  private readonly shardId: string;
  private readonly connectionString: string;
  private readonly products: Map<string, Product> = new Map();

  constructor(shardId: string, connectionString: string) {
    this.shardId = shardId;
    this.connectionString = connectionString;
    console.log(`Initialized shard ${shardId} with connection ${connectionString}`);
  }

  getId(): string {
    return this.shardId;
  }

  async insert(product: Product): Promise<void> {
    console.log(`Inserting product ${product.id} into shard ${this.shardId}`);
    // In a real implementation, this would be a database call
    this.products.set(product.id, { ...product });
  }

  async getById(id: string): Promise<Product | null> {
    console.log(`Getting product ${id} from shard ${this.shardId}`);
    // In a real implementation, this would be a database call
    return this.products.get(id) || null;
  }

  async update(product: Product): Promise<void> {
    if (!this.products.has(product.id)) {
      throw new Error(`Product ${product.id} not found in shard ${this.shardId}`);
    }
    
    console.log(`Updating product ${product.id} in shard ${this.shardId}`);
    this.products.set(product.id, { ...product });
  }

  async delete(id: string): Promise<void> {
    if (!this.products.has(id)) {
      throw new Error(`Product ${id} not found in shard ${this.shardId}`);
    }
    
    console.log(`Deleting product ${id} from shard ${this.shardId}`);
    this.products.delete(id);
  }

  async query(predicate: (product: Product) => boolean): Promise<Product[]> {
    console.log(`Querying products in shard ${this.shardId}`);
    // In a real implementation, this might be optimized based on the predicate
    const results: Product[] = [];
    
    for (const product of this.products.values()) {
      if (predicate(product)) {
        results.push({ ...product });
      }
    }
    
    return results;
  }
}

// Hash-based shard manager
class HashShardManager<T extends ShardedEntity> {
  private shards: Map<string, Shard<T>> = new Map();
  private shardList: Shard<T>[] = [];
  private readonly shardKeyExtractor: (entity: T) => string;
  
  constructor(shards: Shard<T>[], shardKeyExtractor: (entity: T) => string) {
    this.shardList = [...shards];
    
    for (const shard of shards) {
      this.shards.set(shard.getId(), shard);
    }
    
    this.shardKeyExtractor = shardKeyExtractor;
    console.log(`Initialized shard manager with ${shards.length} shards`);
  }

  // Determine which shard should store an entity based on its shard key
  private getShardForEntity(entity: T): Shard<T> {
    const shardKey = this.shardKeyExtractor(entity);
    return this.getShardByKey(shardKey);
  }

  // Get shard by shard key using consistent hashing
  private getShardByKey(shardKey: string): Shard<T> {
    if (!shardKey) {
      throw new Error('Shard key cannot be empty');
    }
    
    // Simple hash function - in production use a proper consistent hashing algorithm
    const hash = this.hashString(shardKey);
    const shardIndex = hash % this.shardList.length;
    
    return this.shardList[shardIndex];
  }

  // Simple string hashing function
  private hashString(str: string): number {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
      const char = str.charCodeAt(i);
      hash = ((hash << 5) - hash) + char;
      hash = hash & hash; // Convert to 32bit integer
    }
    return Math.abs(hash);
  }

  // Insert entity into the appropriate shard
  async insert(entity: T): Promise<void> {
    const shard = this.getShardForEntity(entity);
    console.log(`Routing insert of entity ${entity.id} to shard ${shard.getId()}`);
    await shard.insert(entity);
  }

  // Get entity by ID - requires checking all shards if shard key is unknown
  async getById(id: string, shardKey?: string): Promise<T | null> {
    if (shardKey) {
      // If we know the shard key, we can go directly to the right shard
      const shard = this.getShardByKey(shardKey);
      return shard.getById(id);
    }
    
    // Otherwise, we need to check all shards (fan-out query)
    console.warn(`Performing fan-out query across all shards for ID ${id}`);
    
    for (const shard of this.shardList) {
      const entity = await shard.getById(id);
      if (entity) {
        return entity;
      }
    }
    
    return null;
  }

  // Update entity in the appropriate shard
  async update(entity: T): Promise<void> {
    const shard = this.getShardForEntity(entity);
    console.log(`Routing update of entity ${entity.id} to shard ${shard.getId()}`);
    await shard.update(entity);
  }

  // Delete entity - requires checking all shards if shard key is unknown
  async delete(id: string, shardKey?: string): Promise<void> {
    if (shardKey) {
      // If we know the shard key, we can go directly to the right shard
      const shard = this.getShardByKey(shardKey);
      await shard.delete(id);
      return;
    }
    
    // Otherwise, we need to find it first
    const entity = await this.getById(id);
    if (!entity) {
      throw new Error(`Entity ${id} not found in any shard`);
    }
    
    const shard = this.getShardForEntity(entity);
    await shard.delete(id);
  }

  // Execute a query across all shards
  async query(predicate: (entity: T) => boolean): Promise<T[]> {
    console.log('Executing cross-shard query');
    
    // Execute the query on each shard in parallel
    const queryPromises = this.shardList.map(shard => shard.query(predicate));
    const results = await Promise.all(queryPromises);
    
    // Combine and return all results
    return results.flat();
  }
}

// Example usage
async function demo() {
  // Initialize shards based on product categories
  const shards: ProductShard[] = [
    new ProductShard('electronics', 'mongodb://server1:27017/electronics'),
    new ProductShard('clothing', 'mongodb://server2:27017/clothing'),
    new ProductShard('books', 'mongodb://server3:27017/books'),
    new ProductShard('home', 'mongodb://server4:27017/home')
  ];
  
  // Create shard manager that uses product category as the shard key
  const shardManager = new HashShardManager<Product>(
    shards,
    (product) => product.category
  );
  
  // Example products
  const products: Product[] = [
    {
      id: uuidv4(),
      name: 'Smartphone',
      category: 'electronics',
      price: 599.99,
      stock: 120,
      createdAt: new Date()
    },
    {
      id: uuidv4(),
      name: 'T-shirt',
      category: 'clothing',
      price: 19.99,
      stock: 500,
      createdAt: new Date()
    },
    {
      id: uuidv4(),
      name: 'JavaScript: The Good Parts',
      category: 'books',
      price: 29.99,
      stock: 75,
      createdAt: new Date()
    },
    {
      id: uuidv4(),
      name: 'Coffee Maker',
      category: 'home',
      price: 89.99,
      stock: 30,
      createdAt: new Date()
    }
  ];
  
  // Insert products into the appropriate shards
  console.log('Inserting products...');
  for (const product of products) {
    await shardManager.insert(product);
  }
  
  // Retrieve a product when we know the shard key (category)
  console.log('\nRetrieving a product with known category:');
  const bookId = products[2].id;
  const book = await shardManager.getById(bookId, 'books');
  console.log('Found book:', book);
  
  // Retrieve a product without knowing the shard key
  console.log('\nRetrieving a product with unknown category:');
  const smartphone = await shardManager.getById(products[0].id);
  console.log('Found smartphone:', smartphone);
  
  // Query products across all shards
  console.log('\nQuerying products with price > 50:');
  const expensiveProducts = await shardManager.query(product => product.price > 50);
  console.log(`Found ${expensiveProducts.length} expensive products:`);
  expensiveProducts.forEach(p => console.log(`- ${p.name}: $${p.price}`));
  
  // Update a product
  if (smartphone) {
    console.log('\nUpdating a product:');
    smartphone.price = 549.99;
    smartphone.stock -= 10;
    await shardManager.update(smartphone);
    
    // Verify update
    const updatedSmartphone = await shardManager.getById(smartphone.id, 'electronics');
    console.log('Updated smartphone:', updatedSmartphone);
  }
}

// Run the demo
demo().catch(console.error);
```

## When to Use

- When your database size exceeds the capacity of a single server
- For applications with high throughput requirements
- When you need to distribute data geographically for lower latency
- To improve availability and fault isolation
- For applications with clear data partitioning criteria

## When Not to Use

- When your data size is manageable on a single database
- If your application requires complex transactions across different data entities
- When the overhead of managing shards outweighs the benefits
- If your sharding key choice would lead to uneven data distribution
- For systems where resharding would be prohibitively complex

## Related Patterns

- [Data Replication and Consistency Patterns](11-data-replication-consistency.md)
- [Bulkhead Pattern](05-bulkhead-pattern.md)
- [Cache-Aside Pattern](07-cache-aside-pattern.md)
