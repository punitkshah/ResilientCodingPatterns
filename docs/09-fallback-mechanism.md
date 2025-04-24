# Fallback Mechanism Pattern

## Overview

The Fallback Mechanism pattern provides alternative functionality when a service or operation fails. Instead of failing completely, the application degrades gracefully by executing alternative code paths, returning cached data, or providing simplified functionality.

## Problem Statement

In distributed systems, components will inevitably fail due to:
- Network issues or service outages
- Dependency failures
- Resource exhaustion
- Timeouts
- Deployment issues

When these failures occur, applications need strategies to continue operating, even with reduced functionality, rather than failing completely.

## Solution: Fallback Mechanism Pattern

Implement fallback strategies that:
1. **Detect** failures in primary operations
2. **Switch** to alternative implementations when failures occur
3. **Degrade** functionality gracefully rather than failing completely
4. **Recover** automatically when the primary functionality becomes available again
5. **Communicate** the degraded state to users when appropriate

## Implementation Strategies

### 1. Cached Data Fallback

**Why**: When live data is unavailable, returning slightly stale data is often better than no data.

**Guidelines**:
- Cache successful responses with appropriate TTL
- When live operations fail, return cached data
- Indicate to clients that data may be stale
- Clear cache entries when data is known to be invalid

**C# Example**:

```csharp
public class ProductService
{
    private readonly IProductRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProductService> _logger;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

    public ProductService(
        IProductRepository repository,
        IMemoryCache cache,
        ILogger<ProductService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ProductResult> GetProductAsync(string productId)
    {
        // Generate cache key
        string cacheKey = $"product:{productId}";
        
        try
        {
            // Try to get the latest data
            var product = await _repository.GetProductByIdAsync(productId);
            
            if (product == null)
            {
                return ProductResult.NotFound(productId);
            }
            
            // Cache the successful result
            _cache.Set(cacheKey, product, _cacheDuration);
            
            return ProductResult.Success(product);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving product {ProductId} from repository", productId);
            
            // Try to get from cache
            if (_cache.TryGetValue(cacheKey, out Product cachedProduct))
            {
                _logger.LogInformation("Using cached data for product {ProductId}", productId);
                
                return ProductResult.FromCache(cachedProduct);
            }
            
            // If nothing in cache, return error result
            return ProductResult.Error(
                productId, 
                "Unable to retrieve product information at this time.");
        }
    }
}

public class ProductResult
{
    public bool IsSuccess { get; private set; }
    public bool IsFromCache { get; private set; }
    public bool IsNotFound { get; private set; }
    public bool IsError { get; private set; }
    public string ErrorMessage { get; private set; }
    public Product Data { get; private set; }

    public static ProductResult Success(Product product)
    {
        return new ProductResult
        {
            IsSuccess = true,
            IsFromCache = false,
            IsNotFound = false,
            IsError = false,
            Data = product
        };
    }

    public static ProductResult FromCache(Product product)
    {
        return new ProductResult
        {
            IsSuccess = true,
            IsFromCache = true,
            IsNotFound = false,
            IsError = false,
            Data = product
        };
    }

    public static ProductResult NotFound(string productId)
    {
        return new ProductResult
        {
            IsSuccess = false,
            IsFromCache = false,
            IsNotFound = true,
            IsError = false,
            ErrorMessage = $"Product {productId} not found."
        };
    }

    public static ProductResult Error(string productId, string errorMessage)
    {
        return new ProductResult
        {
            IsSuccess = false,
            IsFromCache = false,
            IsNotFound = false,
            IsError = true,
            ErrorMessage = errorMessage
        };
    }
}
```

### 2. Default Value Fallback

**Why**: For non-critical operations, returning a safe default value can maintain application flow.

**Guidelines**:
- Identify operations where a safe default makes sense
- Design defaults that don't introduce inconsistencies
- Log the fallback for monitoring and analysis
- Consider context-specific defaults rather than generic values

**C# Example**:

```csharp
public class RecommendationService
{
    private readonly IRecommendationEngine _recommendationEngine;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        IRecommendationEngine recommendationEngine,
        IProductRepository productRepository,
        ILogger<RecommendationService> logger)
    {
        _recommendationEngine = recommendationEngine;
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<List<Product>> GetPersonalizedRecommendationsAsync(
        string userId, string category, int count = 5)
    {
        try
        {
            // Try to get personalized recommendations
            var recommendations = await _recommendationEngine.GetRecommendationsAsync(
                userId, category, count);
                
            if (recommendations != null && recommendations.Any())
            {
                return recommendations;
            }
            
            // If no recommendations, fall back to popular items
            _logger.LogInformation(
                "No personalized recommendations found for user {UserId} in category {Category}. " +
                "Falling back to popular items.", userId, category);
                
            return await GetPopularItemsFallbackAsync(category, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting personalized recommendations for user {UserId} in category {Category}. " +
                "Falling back to popular items.", userId, category);
                
            return await GetPopularItemsFallbackAsync(category, count);
        }
    }

    private async Task<List<Product>> GetPopularItemsFallbackAsync(
        string category, int count)
    {
        try
        {
            // Try to get popular items for the category
            return await _productRepository.GetPopularProductsAsync(category, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting popular products for category {Category}. " +
                "Falling back to empty list.", category);
                
            // Ultimate fallback - empty list rather than throwing exception
            return new List<Product>();
        }
    }
}
```

### 3. Alternative Service Implementation

**Why**: For critical functionality, having a simpler but more reliable alternative implementation improves resilience.

**Guidelines**:
- Implement simplified versions of critical services
- Focus on reliability over features in fallback implementations
- Use feature flags to control fallback behavior
- Test fallback paths as part of regular testing

**C# Example**:

```csharp
public interface IPaymentProcessor
{
    Task<PaymentResult> ProcessPaymentAsync(
        string orderId, decimal amount, PaymentDetails paymentDetails);
}

// Primary implementation using a third-party payment provider
public class StripePaymentProcessor : IPaymentProcessor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StripePaymentProcessor> _logger;

    public StripePaymentProcessor(HttpClient httpClient, ILogger<StripePaymentProcessor> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        string orderId, decimal amount, PaymentDetails paymentDetails)
    {
        try
        {
            // Actual implementation would include Stripe API calls
            var response = await _httpClient.PostAsJsonAsync(
                "https://api.stripe.com/v1/charges",
                new
                {
                    amount = (int)(amount * 100), // Stripe uses cents
                    currency = "usd",
                    source = paymentDetails.Token,
                    description = $"Order {orderId}"
                });

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<StripeChargeResponse>();

            return new PaymentResult
            {
                Success = true,
                TransactionId = result.Id,
                ProviderName = "Stripe",
                Message = "Payment processed successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment with Stripe for order {OrderId}", orderId);
            throw;
        }
    }
}

// Fallback implementation using a different provider
public class FallbackPaymentProcessor : IPaymentProcessor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FallbackPaymentProcessor> _logger;

    public FallbackPaymentProcessor(HttpClient httpClient, ILogger<FallbackPaymentProcessor> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        string orderId, decimal amount, PaymentDetails paymentDetails)
    {
        try
        {
            // Simpler implementation using backup payment provider
            var response = await _httpClient.PostAsJsonAsync(
                "https://api.backup-payments.com/process",
                new
                {
                    orderId,
                    amount,
                    cardNumber = paymentDetails.CardNumber,
                    expiryMonth = paymentDetails.ExpiryMonth,
                    expiryYear = paymentDetails.ExpiryYear
                });

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<BackupPaymentResponse>();

            return new PaymentResult
            {
                Success = true,
                TransactionId = result.TransactionId,
                ProviderName = "BackupPayments",
                Message = "Payment processed via backup provider"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment with backup provider for order {OrderId}", orderId);
            throw;
        }
    }
}

// Resilient service that uses both processors with fallback logic
public class ResilientPaymentService
{
    private readonly IPaymentProcessor _primaryProcessor;
    private readonly IPaymentProcessor _fallbackProcessor;
    private readonly ILogger<ResilientPaymentService> _logger;
    private readonly CircuitBreakerPolicy _circuitBreaker;

    public ResilientPaymentService(
        StripePaymentProcessor primaryProcessor,
        FallbackPaymentProcessor fallbackProcessor,
        ILogger<ResilientPaymentService> logger)
    {
        _primaryProcessor = primaryProcessor;
        _fallbackProcessor = fallbackProcessor;
        _logger = logger;
        
        // Define circuit breaker for primary processor
        _circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 2,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: (ex, breakDuration) =>
                {
                    _logger.LogWarning(ex, 
                        "Circuit breaker tripped for primary payment processor. " +
                        "Using fallback for {BreakDuration}.", breakDuration);
                },
                onReset: () =>
                {
                    _logger.LogInformation(
                        "Circuit breaker reset. Primary payment processor back online.");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation(
                        "Circuit breaker half-open. Testing primary payment processor.");
                }
            );
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        string orderId, decimal amount, PaymentDetails paymentDetails)
    {
        try
        {
            // Try primary processor with circuit breaker
            return await _circuitBreaker.ExecuteAsync(async () =>
                await _primaryProcessor.ProcessPaymentAsync(orderId, amount, paymentDetails));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Primary payment processor failed for order {OrderId}. Using fallback.", orderId);
                
            try
            {
                // Use fallback processor
                var result = await _fallbackProcessor.ProcessPaymentAsync(
                    orderId, amount, paymentDetails);
                
                // Add notification in result
                result.UsedFallback = true;
                result.Message = "Payment processed using fallback processor due to issues with primary processor.";
                
                return result;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, 
                    "Both primary and fallback payment processors failed for order {OrderId}", orderId);
                
                throw new PaymentProcessingException(
                    "All payment processors failed. Please try again later.",
                    fallbackEx);
            }
        }
    }
}
```

### 4. Feature Degradation

**Why**: When a system is under stress, disabling non-critical features can maintain core functionality.

**Guidelines**:
- Categorize features by criticality
- Implement mechanisms to selectively disable features
- Design UI to gracefully handle missing features
- Use health checks to drive feature availability

**C# Example with Feature Management**:

```csharp
public class EcommerceController : Controller
{
    private readonly IProductService _productService;
    private readonly IRecommendationService _recommendationService;
    private readonly IReviewService _reviewService;
    private readonly IFeatureManager _featureManager;
    private readonly IHealthCheckService _healthCheckService;
    private readonly ILogger<EcommerceController> _logger;

    public EcommerceController(
        IProductService productService,
        IRecommendationService recommendationService,
        IReviewService reviewService,
        IFeatureManager featureManager,
        IHealthCheckService healthCheckService,
        ILogger<EcommerceController> logger)
    {
        _productService = productService;
        _recommendationService = recommendationService;
        _reviewService = reviewService;
        _featureManager = featureManager;
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    public async Task<IActionResult> ProductDetail(string productId)
    {
        // Always try to get the product (core functionality)
        var productResult = await _productService.GetProductAsync(productId);
        
        if (!productResult.IsSuccess)
        {
            return productResult.IsNotFound 
                ? NotFound() 
                : StatusCode(500, "Unable to retrieve product information");
        }
        
        var viewModel = new ProductDetailViewModel
        {
            Product = productResult.Data,
            IsProductFromCache = productResult.IsFromCache
        };
        
        // Check system health and adjust feature availability
        var health = await _healthCheckService.CheckHealthAsync();
        if (health.Status == HealthStatus.Degraded)
        {
            // Disable non-critical features when system is degraded
            await _featureManager.SetFeatureEnabledAsync("ProductReviews", false);
            await _featureManager.SetFeatureEnabledAsync("Recommendations", false);
            
            viewModel.SystemDegraded = true;
            viewModel.SystemMessage = "Some features are temporarily unavailable.";
        }
        
        // Add recommendations if feature is enabled
        if (await _featureManager.IsEnabledAsync("Recommendations"))
        {
            try
            {
                var recommendations = await _recommendationService
                    .GetRecommendationsAsync(productId, 5);
                    
                viewModel.Recommendations = recommendations;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to load recommendations for product {ProductId}", productId);
                
                // Don't fail the page load for missing recommendations
                viewModel.SystemMessage = "Product recommendations are temporarily unavailable.";
            }
        }
        
        // Add reviews if feature is enabled
        if (await _featureManager.IsEnabledAsync("ProductReviews"))
        {
            try
            {
                var reviews = await _reviewService.GetReviewsAsync(productId);
                viewModel.Reviews = reviews;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to load reviews for product {ProductId}", productId);
                
                // Don't fail the page load for missing reviews
                if (string.IsNullOrEmpty(viewModel.SystemMessage))
                {
                    viewModel.SystemMessage = "Product reviews are temporarily unavailable.";
                }
                else
                {
                    viewModel.SystemMessage += " Product reviews are temporarily unavailable.";
                }
            }
        }
        
        return View(viewModel);
    }
}
```

### 5. Queue-Based Fallback

**Why**: For operations that don't need immediate processing, queueing for later execution preserves consistency.

**Guidelines**:
- Identify operations that can be deferred
- Implement reliable queuing with appropriate storage
- Set up queue processing with retries and error handling
- Provide status indicators for queued operations

**C# Example**:

```csharp
public class OrderProcessingService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentService _paymentService;
    private readonly IInventoryService _inventoryService;
    private readonly IBackgroundQueue _backgroundQueue;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        IOrderRepository orderRepository,
        IPaymentService paymentService,
        IInventoryService inventoryService,
        IBackgroundQueue backgroundQueue,
        ILogger<OrderProcessingService> logger)
    {
        _orderRepository = orderRepository;
        _paymentService = paymentService;
        _inventoryService = inventoryService;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest orderRequest, CancellationToken cancellationToken)
    {
        // Step 1: Create the order record
        var order = await CreateOrderRecordAsync(orderRequest);
        
        try
        {
            // Step 2: Process payment (critical, must succeed)
            var paymentResult = await _paymentService.ProcessPaymentAsync(
                order.Id, order.TotalAmount, orderRequest.PaymentDetails);
                
            if (!paymentResult.Success)
            {
                return OrderResult.Failed(
                    order.Id, "Payment processing failed: " + paymentResult.Message);
            }
            
            order.PaymentId = paymentResult.TransactionId;
            
            // Step 3: Try to update inventory immediately
            try
            {
                var inventoryResult = await _inventoryService.AllocateInventoryAsync(
                    order.Id, order.Items);
                    
                if (inventoryResult.Success)
                {
                    // All good - standard path
                    order.Status = OrderStatus.Processing;
                    order.StatusMessage = "Order confirmed and being processed.";
                }
                else
                {
                    // Queue for later inventory processing
                    order.Status = OrderStatus.PaymentCompleted;
                    order.StatusMessage = "Payment processed. Inventory allocation pending.";
                    
                    // Add to background queue for retry
                    _backgroundQueue.QueueBackgroundWorkItem(async token =>
                    {
                        await ProcessInventoryAllocationAsync(order.Id, token);
                    });
                    
                    _logger.LogWarning(
                        "Inventory allocation for order {OrderId} failed immediately. " +
                        "Queued for background processing.", order.Id);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the order
                _logger.LogError(ex, 
                    "Error allocating inventory for order {OrderId}. " +
                    "Queued for background processing.", order.Id);
                
                // Queue for later inventory processing
                order.Status = OrderStatus.PaymentCompleted;
                order.StatusMessage = "Payment processed. Inventory allocation in progress.";
                
                // Add to background queue for retry
                _backgroundQueue.QueueBackgroundWorkItem(async token =>
                {
                    await ProcessInventoryAllocationAsync(order.Id, token);
                });
            }
            
            // Save order state
            await _orderRepository.UpdateOrderAsync(order);
            
            return OrderResult.Success(
                order.Id, 
                order.Status, 
                order.StatusMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order {OrderId}", order.Id);
            
            // Update order state to reflect error
            order.Status = OrderStatus.Error;
            order.StatusMessage = "An error occurred while processing your order. " +
                                  "Our team has been notified and will contact you shortly.";
                                  
            await _orderRepository.UpdateOrderAsync(order);
            
            return OrderResult.Failed(
                order.Id, 
                "Order processing error: Please contact customer support.");
        }
    }

    private async Task<Order> CreateOrderRecordAsync(OrderRequest orderRequest)
    {
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            UserId = orderRequest.UserId,
            Items = orderRequest.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList(),
            TotalAmount = orderRequest.Items.Sum(i => i.Quantity * i.UnitPrice),
            ShippingAddress = orderRequest.ShippingAddress,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        
        await _orderRepository.CreateOrderAsync(order);
        return order;
    }

    private async Task ProcessInventoryAllocationAsync(string orderId, CancellationToken cancellationToken)
    {
        // Retry policy for inventory allocation
        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                5, // 5 retries
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                onRetry: (ex, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(ex, 
                        "Retry {RetryCount} for inventory allocation for order {OrderId} " +
                        "after {RetryDelay}s", retryCount, orderId, timespan.TotalSeconds);
                });
        
        try
        {
            // Get current order state
            var order = await _orderRepository.GetOrderByIdAsync(orderId);
            
            if (order == null)
            {
                _logger.LogError("Order {OrderId} not found for inventory allocation", orderId);
                return;
            }
            
            // If already processed, skip
            if (order.Status == OrderStatus.Processing || order.Status == OrderStatus.Completed)
            {
                _logger.LogInformation(
                    "Order {OrderId} already has inventory allocated. Status: {Status}", 
                    orderId, order.Status);
                return;
            }
            
            // Try to allocate inventory with retry policy
            await retryPolicy.ExecuteAsync(async () =>
            {
                var result = await _inventoryService.AllocateInventoryAsync(orderId, order.Items);
                
                if (!result.Success)
                {
                    throw new InventoryAllocationException(
                        $"Inventory allocation failed for order {orderId}: {result.Message}");
                }
                
                // Update order state
                order.Status = OrderStatus.Processing;
                order.StatusMessage = "Order confirmed and being processed.";
                await _orderRepository.UpdateOrderAsync(order);
                
                _logger.LogInformation(
                    "Successfully allocated inventory for order {OrderId} " +
                    "after background processing", orderId);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "All retries failed for inventory allocation for order {OrderId}", orderId);
                
            // If we've exhausted all retries, update the order status
            // and alert support for manual intervention
            try
            {
                var order = await _orderRepository.GetOrderByIdAsync(orderId);
                
                order.Status = OrderStatus.NeedsAttention;
                order.StatusMessage = 
                    "We're having trouble confirming inventory for your order. " +
                    "A customer service representative will contact you shortly.";
                
                await _orderRepository.UpdateOrderAsync(order);
                
                // In a real system, we'd also alert customer service
                // AlertCustomerService(orderId, ex.Message);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, 
                    "Failed to update order {OrderId} status after inventory allocation failure", 
                    orderId);
            }
        }
    }
}
```

## TypeScript Implementation Example

```typescript
import { Logger } from './logger';
import { CircuitBreaker } from 'opossum';

// Product data structure
interface Product {
  id: string;
  name: string;
  description: string;
  price: number;
  inStock: boolean;
  lastUpdated: Date;
}

// Product service result with status information
interface ProductResult {
  success: boolean;
  fromCache: boolean;
  notFound: boolean;
  isError: boolean;
  errorMessage?: string;
  data?: Product;
}

// Repository interface
interface ProductRepository {
  getProductById(productId: string): Promise<Product | null>;
}

// Cache service
class CacheService {
  private cache: Map<string, { data: any, expiry: Date }> = new Map();
  
  set<T>(key: string, data: T, ttlSeconds: number): void {
    const expiry = new Date();
    expiry.setSeconds(expiry.getSeconds() + ttlSeconds);
    this.cache.set(key, { data, expiry });
  }
  
  get<T>(key: string): T | null {
    const item = this.cache.get(key);
    if (!item) return null;
    
    if (item.expiry < new Date()) {
      this.cache.delete(key);
      return null;
    }
    
    return item.data as T;
  }
  
  remove(key: string): void {
    this.cache.delete(key);
  }
}

// Product service with fallback mechanisms
class ProductService {
  private circuitBreaker: CircuitBreaker;
  
  constructor(
    private repository: ProductRepository,
    private cache: CacheService,
    private logger: Logger
  ) {
    // Set up circuit breaker
    this.circuitBreaker = new CircuitBreaker(async (productId: string) => {
      return await this.repository.getProductById(productId);
    }, {
      timeout: 3000, // 3 seconds
      errorThresholdPercentage: 50,
      resetTimeout: 30000, // 30 seconds
      name: 'productRepositoryCircuitBreaker'
    });
    
    // Circuit breaker events
    this.circuitBreaker.on('open', () => {
      this.logger.warn('Product repository circuit breaker opened');
    });
    
    this.circuitBreaker.on('halfOpen', () => {
      this.logger.info('Product repository circuit breaker half-open');
    });
    
    this.circuitBreaker.on('close', () => {
      this.logger.info('Product repository circuit breaker closed');
    });
  }
  
  async getProduct(productId: string): Promise<ProductResult> {
    const cacheKey = `product:${productId}`;
    
    try {
      // Try circuit breaker protected call
      const product = await this.circuitBreaker.fire(productId);
      
      if (!product) {
        return {
          success: false,
          fromCache: false,
          notFound: true,
          isError: false
        };
      }
      
      // Cache the successful result
      this.cache.set(cacheKey, product, 1800); // 30 minutes
      
      return {
        success: true,
        fromCache: false,
        notFound: false,
        isError: false,
        data: product
      };
    } catch (error) {
      this.logger.error(`Error retrieving product ${productId}: ${error.message}`);
      
      // Fallback to cache
      const cachedProduct = this.cache.get<Product>(cacheKey);
      
      if (cachedProduct) {
        this.logger.info(`Using cached data for product ${productId}`);
        
        return {
          success: true,
          fromCache: true,
          notFound: false,
          isError: false,
          data: cachedProduct
        };
      }
      
      // No cache fallback available
      return {
        success: false,
        fromCache: false,
        notFound: false,
        isError: true,
        errorMessage: 'Unable to retrieve product information at this time.'
      };
    }
  }
}

// Feature flag service for feature degradation
class FeatureToggleService {
  private features: Map<string, boolean> = new Map();
  
  constructor(
    private logger: Logger
  ) {
    // Default all features to enabled
    this.features.set('recommendations', true);
    this.features.set('reviews', true);
    this.features.set('inventory', true);
    this.features.set('relatedProducts', true);
  }
  
  isEnabled(featureName: string): boolean {
    return this.features.get(featureName) ?? false;
  }
  
  setFeatureEnabled(featureName: string, enabled: boolean): void {
    this.features.set(featureName, enabled);
    this.logger.info(`Feature ${featureName} ${enabled ? 'enabled' : 'disabled'}`);
  }
  
  // Gracefully degrade features based on system health
  degradeFeatures(healthStatus: string): void {
    if (healthStatus === 'critical') {
      // Only keep essential features
      this.setFeatureEnabled('recommendations', false);
      this.setFeatureEnabled('reviews', false);
      this.setFeatureEnabled('relatedProducts', false);
      // Keep inventory as it's core functionality
      this.setFeatureEnabled('inventory', true);
      
      this.logger.warn('System health critical - degraded to essential features only');
    } else if (healthStatus === 'degraded') {
      // Keep core features, disable nice-to-have features
      this.setFeatureEnabled('recommendations', false);
      this.setFeatureEnabled('relatedProducts', false);
      this.setFeatureEnabled('reviews', true);
      this.setFeatureEnabled('inventory', true);
      
      this.logger.warn('System health degraded - disabled non-essential features');
    } else {
      // Everything should be enabled
      this.setFeatureEnabled('recommendations', true);
      this.setFeatureEnabled('reviews', true);
      this.setFeatureEnabled('relatedProducts', true);
      this.setFeatureEnabled('inventory', true);
      
      this.logger.info('System health normal - all features enabled');
    }
  }
}

// Example usage
async function demonstrateFallbackPatterns(): Promise<void> {
  const logger = new ConsoleLogger();
  const cache = new CacheService();
  const repository = new DatabaseProductRepository(logger);
  const productService = new ProductService(repository, cache, logger);
  const featureService = new FeatureToggleService(logger);
  
  // Normal operation - successful retrieval
  logger.info('=== Normal Operation ===');
  const result1 = await productService.getProduct('product-123');
  if (result1.success) {
    logger.info('Product retrieved successfully');
    if (result1.fromCache) {
      logger.info('Data from cache');
    }
    logger.info(result1.data);
  }
  
  // Simulate repository failure
  logger.info('\n=== Repository Failure with Cache Fallback ===');
  repository.simulateFailure(true);
  const result2 = await productService.getProduct('product-123');
  if (result2.success) {
    logger.info('Product retrieved from cache during failure');
    logger.info(result2.data);
  } else {
    logger.error('Fallback failed');
  }
  
  // Simulate system degradation
  logger.info('\n=== System Degradation ===');
  featureService.degradeFeatures('degraded');
  
  // Check what features are available in degraded mode
  logger.info('Feature status in degraded mode:');
  logger.info(`- Recommendations: ${featureService.isEnabled('recommendations')}`);
  logger.info(`- Reviews: ${featureService.isEnabled('reviews')}`);
  logger.info(`- Related Products: ${featureService.isEnabled('relatedProducts')}`);
  logger.info(`- Inventory: ${featureService.isEnabled('inventory')}`);
  
  // Restore normal operation
  logger.info('\n=== Restoring Normal Operation ===');
  repository.simulateFailure(false);
  featureService.degradeFeatures('normal');
  const result3 = await productService.getProduct('product-123');
  if (result3.success && !result3.fromCache) {
    logger.info('System restored to normal operation');
    logger.info(result3.data);
  }
}
```

## Azure-Specific Implementation

In Azure applications, fallback mechanisms can leverage native services:

### Azure Front Door Fallbacks

For global applications, Azure Front Door can route requests to healthy backends:

```json
{
  "properties": {
    "routingRules": [
      {
        "name": "HttpRule",
        "properties": {
          "frontendEndpoints": [ "frontendEndpoint1" ],
          "acceptedProtocols": [ "Http", "Https" ],
          "patternsToMatch": [ "/*" ],
          "routeConfiguration": {
            "@odata.type": "#Microsoft.Azure.FrontDoor.Models.FrontdoorForwardingConfiguration",
            "forwardingProtocol": "HttpsOnly",
            "backendPool": {
              "id": "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Network/frontDoors/{frontDoorName}/backendPools/primaryBackend"
            }
          },
          "enabledState": "Enabled"
        }
      }
    ],
    "backendPools": [
      {
        "name": "primaryBackend",
        "properties": {
          "backends": [
            {
              "address": "primary-service.azurewebsites.net",
              "httpPort": 80,
              "httpsPort": 443,
              "weight": 100,
              "priority": 1
            },
            {
              "address": "fallback-service.azurewebsites.net",
              "httpPort": 80,
              "httpsPort": 443,
              "weight": 100,
              "priority": 2
            }
          ],
          "loadBalancingSettings": {
            "id": "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Network/frontDoors/{frontDoorName}/loadBalancingSettings/loadBalancingSettings1"
          },
          "healthProbeSettings": {
            "id": "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Network/frontDoors/{frontDoorName}/healthProbeSettings/healthProbeSettings1"
          }
        }
      }
    ]
  }
}
```

### Azure API Management Policies

API Management policies can implement fallbacks for backend services:

```xml
<policies>
  <inbound>
    <base />
    <set-variable name="fallbackResponseRequired" value="@(false)" />
  </inbound>
  <backend>
    <retry condition="@(context.Response != null && (context.Response.StatusCode >= 500 || context.Response.StatusCode == 408))" 
           count="2" interval="5" max-interval="15" delta="2" first-fast-retry="true">
      <forward-request timeout="10" />
    </retry>
    <choose>
      <when condition="@(context.Response == null || context.Response.StatusCode >= 500 || context.Response.StatusCode == 408)">
        <set-variable name="fallbackResponseRequired" value="@(true)" />
        <set-variable name="primaryResponseStatusCode" 
                     value="@(context.Response != null ? context.Response.StatusCode.ToString() : "No response")" />
      </when>
    </choose>
  </backend>
  <outbound>
    <base />
    <choose>
      <when condition="@(context.Variables.GetValueOrDefault<bool>("fallbackResponseRequired"))">
        <!-- Log the failure -->
        <set-variable name="requestId" value="@(Guid.NewGuid())" />
        <log-to-eventhub logger-id="backend-failures" partition-id="0">
            @{
                return new JObject(
                    new JProperty("requestId", context.Variables["requestId"]),
                    new JProperty("api", context.Api.Name),
                    new JProperty("operation", context.Operation.Name),
                    new JProperty("primaryResponseStatusCode", context.Variables["primaryResponseStatusCode"]),
                    new JProperty("utcTime", DateTime.UtcNow.ToString("o"))
                ).ToString();
            }
        </log-to-eventhub>
        
        <!-- Return cached response if appropriate operation -->
        <cache-lookup-value key="@("fallback-" + context.Request.Url.Path)" variable-name="fallbackContent" />
        <choose>
          <when condition="@(context.Variables.GetValueOrDefault<string>("fallbackContent") != null)">
            <set-status code="200" reason="OK (Fallback)" />
            <set-header name="Content-Type" exists-action="override">
              <value>application/json</value>
            </set-header>
            <set-header name="X-Cache-Fallback" exists-action="override">
              <value>true</value>
            </set-header>
            <set-body>@(context.Variables["fallbackContent"])</set-body>
          </when>
          <otherwise>
            <!-- No cached fallback available -->
            <set-status code="503" reason="Service Unavailable" />
            <set-header name="Content-Type" exists-action="override">
              <value>application/json</value>
            </set-header>
            <set-body>{"error": "Service temporarily unavailable.", "requestId": "@(context.Variables["requestId"])"}</set-body>
          </otherwise>
        </choose>
      </when>
      <otherwise>
        <!-- For GET operations, cache the successful response for fallback -->
        <choose>
          <when condition="@(context.Request.Method.Equals("GET") && context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)">
            <cache-store-value key="@("fallback-" + context.Request.Url.Path)" value="@(context.Response.Body.As<string>(preserveContent: true))" duration="3600" />
          </when>
        </choose>
      </otherwise>
    </choose>
  </outbound>
</policies>
```

### Cosmos DB Multi-Region Fallback

For globally distributed applications, Cosmos DB's multi-region capability provides built-in fallback:

```csharp
// Configure the connection policy for automatic regional failover
var connectionPolicy = new ConnectionPolicy
{
    ConnectionMode = ConnectionMode.Direct,
    ConnectionProtocol = Protocol.Tcp,
    UseMultipleWriteLocations = true, // Use this for multi-master setups
    EnableEndpointDiscovery = true,
    PreferredLocations = new List<string>
    {
        "East US",    // Primary region
        "West US",    // First fallback region
        "Central US"  // Second fallback region
    }
};

// Initialize the Cosmos client with the connection policy
var client = new CosmosClient(
    accountEndpoint: configuration["CosmosDb:Endpoint"],
    authKeyOrResourceToken: configuration["CosmosDb:Key"],
    clientOptions: new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Direct,
        ApplicationRegion = "East US",
        EnableContentResponseOnWrite = false,
        AllowBulkExecution = true,
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });
```

## When to Use

- When application resilience is critical
- For systems with unpredictable dependencies
- In microservices architectures where partial failure is expected
- For user-facing applications that must remain functional
- When service-level agreements (SLAs) require high availability

## When Not to Use

- When data consistency is more important than availability
- For simple applications with minimal external dependencies
- When fallback behavior might lead to incorrect business outcomes
- If the added complexity outweighs the resilience benefits

## Related Patterns

- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Cache-Aside Pattern](01-cache-aside-pattern.md)
- [Retry Pattern](02-retry-pattern.md)
- [Bulkhead Pattern](05-bulkhead-pattern.md)
- [Queue-Based Load Leveling](08-queue-based-load-leveling.md)

## Next Steps

Continue to the [Ability to Scale to Different SKUs](ability-to-scale-different-skus.md) pattern to learn how to design applications that can dynamically adapt to different service tiers.
