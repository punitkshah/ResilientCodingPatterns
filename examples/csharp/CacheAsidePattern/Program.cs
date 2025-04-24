using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheAsidePattern
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup DI container
            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information))
                .AddMemoryCache()
                .AddSingleton<IProductRepository, DemoProductRepository>()
                .AddSingleton<ProductService>()
                .BuildServiceProvider();

            var productService = serviceProvider.GetRequiredService<ProductService>();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Simulate application workflow with cache-aside pattern
            logger.LogInformation("Cache-Aside Pattern Demo\n");

            // First retrieval - Cache miss, fetches from repository
            logger.LogInformation("Fetching product first time (expect cache miss):");
            var product1 = await productService.GetProductByIdAsync("p-001");
            DisplayProduct(product1, logger);

            // Second retrieval - Cache hit
            logger.LogInformation("\nFetching same product again (expect cache hit):");
            var product2 = await productService.GetProductByIdAsync("p-001");
            DisplayProduct(product2, logger);

            // Update product - invalidates cache
            logger.LogInformation("\nUpdating product:");
            product2.Price = 129.99m;
            product2.StockQuantity = 42;
            await productService.UpdateProductAsync(product2);

            // Fetch after update - Cache miss again
            logger.LogInformation("\nFetching product after update (expect cache miss):");
            var product3 = await productService.GetProductByIdAsync("p-001");
            DisplayProduct(product3, logger);

            // Fetch another product that doesn't exist
            logger.LogInformation("\nFetching non-existent product:");
            var nonExistentProduct = await productService.GetProductByIdAsync("p-999");
            if (nonExistentProduct == null)
            {
                logger.LogInformation("Product not found.");
            }

            // Demonstrate cache expiration
            logger.LogInformation("\nDemonstrating cache expiration:");
            logger.LogInformation("Fetching product with short TTL:");
            var shortTtlProduct = await productService.GetProductByIdAsync("p-002", TimeSpan.FromSeconds(2));
            DisplayProduct(shortTtlProduct, logger);

            logger.LogInformation("Waiting for cache to expire (3 seconds)...");
            await Task.Delay(3000);

            logger.LogInformation("Fetching product again after TTL expired (expect cache miss):");
            var expiredCacheProduct = await productService.GetProductByIdAsync("p-002");
            DisplayProduct(expiredCacheProduct, logger);

            logger.LogInformation("\nCache-Aside Pattern demo completed.");
        }

        static void DisplayProduct(Product? product, ILogger logger)
        {
            if (product == null)
            {
                logger.LogInformation("Product is null");
                return;
            }

            logger.LogInformation($"Product: {product.Id} - {product.Name}");
            logger.LogInformation($"  Price: ${product.Price:F2}, Stock: {product.StockQuantity}");
            logger.LogInformation($"  Last Updated: {product.LastUpdated}");
        }
    }

    // Product entity
    public class Product
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    // Repository interface
    public interface IProductRepository
    {
        Task<Product?> GetProductByIdAsync(string productId);
        Task UpdateProductAsync(Product product);
    }

    // Demo product repository that simulates a database
    public class DemoProductRepository : IProductRepository
    {
        private readonly Dictionary<string, Product> _products = new();
        private readonly ILogger<DemoProductRepository> _logger;
        private readonly Random _random = new Random();

        public DemoProductRepository(ILogger<DemoProductRepository> logger)
        {
            _logger = logger;

            // Initialize with sample data
            _products["p-001"] = new Product
            {
                Id = "p-001",
                Name = "Azure Cloud Services Handbook",
                Description = "Complete guide to Azure cloud services",
                Price = 99.99m,
                StockQuantity = 50,
                LastUpdated = DateTime.UtcNow
            };

            _products["p-002"] = new Product
            {
                Id = "p-002",
                Name = "Cloud Resilience Patterns",
                Description = "Implementation patterns for resilient cloud applications",
                Price = 79.99m,
                StockQuantity = 30,
                LastUpdated = DateTime.UtcNow
            };
        }

        public async Task<Product?> GetProductByIdAsync(string productId)
        {
            // Simulate network latency for database access
            await Task.Delay(_random.Next(200, 500));
            
            _logger.LogInformation($"Repository: Fetching product {productId} from database");
            
            if (_products.TryGetValue(productId, out var product))
            {
                // Return a copy to avoid unintended modifications
                return new Product
                {
                    Id = product.Id,
                    Name = product.Name,
                    Description = product.Description,
                    Price = product.Price,
                    StockQuantity = product.StockQuantity,
                    LastUpdated = product.LastUpdated
                };
            }
            
            return null;
        }

        public async Task UpdateProductAsync(Product product)
        {
            // Simulate network latency for database access
            await Task.Delay(_random.Next(200, 500));
            
            _logger.LogInformation($"Repository: Updating product {product.Id} in database");
            
            if (_products.ContainsKey(product.Id))
            {
                product.LastUpdated = DateTime.UtcNow;
                _products[product.Id] = new Product
                {
                    Id = product.Id,
                    Name = product.Name,
                    Description = product.Description,
                    Price = product.Price,
                    StockQuantity = product.StockQuantity,
                    LastUpdated = product.LastUpdated
                };
            }
            else
            {
                throw new KeyNotFoundException($"Product with ID {product.Id} not found");
            }
        }
    }

    // Service that implements the Cache-Aside pattern
    public class ProductService
    {
        private readonly IMemoryCache _cache;
        private readonly IProductRepository _repository;
        private readonly ILogger<ProductService> _logger;
        private readonly TimeSpan _defaultCacheDuration = TimeSpan.FromMinutes(10);

        public ProductService(
            IMemoryCache cache,
            IProductRepository repository,
            ILogger<ProductService> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Product?> GetProductByIdAsync(string productId, TimeSpan? cacheDuration = null)
        {
            // Step 1: Try to get the item from the cache
            string cacheKey = $"product:{productId}";
            
            if (_cache.TryGetValue(cacheKey, out Product? product))
            {
                _logger.LogInformation($"Cache hit for product {productId}");
                return product;
            }
            
            _logger.LogInformation($"Cache miss for product {productId}");
            
            // Step 2: On cache miss, get from the repository
            product = await _repository.GetProductByIdAsync(productId);
            
            if (product == null)
            {
                _logger.LogInformation($"Product {productId} not found in repository");
                return null;
            }
            
            // Step 3: Add to cache
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = cacheDuration ?? _defaultCacheDuration,
                Priority = CacheItemPriority.Normal
            };
            
            _cache.Set(cacheKey, product, options);
            _logger.LogInformation($"Added product {productId} to cache with TTL {(cacheDuration ?? _defaultCacheDuration).TotalSeconds} seconds");
            
            return product;
        }

        public async Task UpdateProductAsync(Product product)
        {
            // Update in repository
            await _repository.UpdateProductAsync(product);
            
            // Invalidate cache
            string cacheKey = $"product:{product.Id}";
            _cache.Remove(cacheKey);
            _logger.LogInformation($"Invalidated cache for product {product.Id}");
        }
    }
}
