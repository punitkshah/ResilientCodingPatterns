# Graceful Degradation Pattern

## Overview

The Graceful Degradation pattern involves designing systems that can reduce functionality rather than completely fail when dependent services are unavailable or experiencing issues. It ensures that the core functions of a system remain available even when some components fail.

## Problem Statement

Cloud applications typically depend on multiple services. When a dependency fails, the traditional approach often leads to a cascade of failures that can bring down the entire system, even when many components are still working properly.

## Solution: Graceful Degradation

Design your application to continue functioning when dependencies fail, even if at a reduced level of functionality. This pattern involves:

1. **Identifying core vs. non-core functionality**
2. **Implementing fallbacks for critical features**
3. **Clearly communicating degraded functionality to users**
4. **Monitoring degraded states to ensure recovery**

## Benefits

- Improved user experience during partial system failures
- Higher overall system availability
- Clear visibility into system health and dependencies
- Better resilience against cascading failures

## Implementation Considerations

- Categorize features by criticality and dependencies
- Design UIs that can adapt to missing functionality
- Consider impacts on data consistency during degraded operation
- Test degraded states regularly as part of resilience testing

## Code Example: Graceful Degradation in C#

```csharp
public class ProductCatalogController : Controller
{
    private readonly IProductService _productService;
    private readonly IRecommendationService _recommendationService;
    private readonly IPricingService _pricingService;
    private readonly ILogger<ProductCatalogController> _logger;
    private readonly CircuitBreakerPolicy _recommendationCircuit;
    private readonly CircuitBreakerPolicy _pricingCircuit;

    public ProductCatalogController(
        IProductService productService,
        IRecommendationService recommendationService,
        IPricingService pricingService,
        ILogger<ProductCatalogController> logger)
    {
        _productService = productService;
        _recommendationService = recommendationService;
        _pricingService = pricingService;
        _logger = logger;
        
        // Set up circuit breakers for non-critical services
        _recommendationCircuit = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1));
            
        _pricingCircuit = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1));
    }
    
    public async Task<IActionResult> ProductDetails(int productId)
    {
        // Core functionality - we can't degrade this
        var product = await _productService.GetProductAsync(productId);
        if (product == null)
        {
            return NotFound();
        }
        
        var viewModel = new ProductDetailViewModel
        {
            Product = product,
            // Default values for degradable features
            RelatedProducts = Array.Empty<Product>(),
            DynamicPricing = null,
            ServiceStatus = new Dictionary<string, bool>
            {
                ["Recommendations"] = true,
                ["DynamicPricing"] = true
            }
        };
        
        // Non-core functionality 1: Recommendations - can be degraded
        try
        {
            viewModel.RelatedProducts = await _recommendationCircuit.ExecuteAsync(
                () => _recommendationService.GetRelatedProductsAsync(productId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recommendations service unavailable. Showing product without recommendations.");
            viewModel.ServiceStatus["Recommendations"] = false;
        }
        
        // Non-core functionality 2: Dynamic pricing - can be degraded
        try
        {
            viewModel.DynamicPricing = await _pricingCircuit.ExecuteAsync(
                () => _pricingService.GetDynamicPricingAsync(productId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dynamic pricing service unavailable. Using standard pricing.");
            viewModel.ServiceStatus["DynamicPricing"] = false;
        }
        
        return View(viewModel);
    }
}
```

## Code Example: Graceful Degradation in TypeScript

```typescript
class ProductService {
  private readonly productApi: ProductApi;
  private readonly recommendationApi: RecommendationApi;
  private readonly reviewsApi: ReviewsApi;
  private readonly circuitBreaker: CircuitBreaker;

  constructor(
    productApi: ProductApi,
    recommendationApi: RecommendationApi,
    reviewsApi: ReviewsApi
  ) {
    this.productApi = productApi;
    this.recommendationApi = recommendationApi;
    this.reviewsApi = reviewsApi;
    
    // Configure circuit breakers for non-critical services
    this.circuitBreaker = new CircuitBreaker({
      failureThreshold: 3,
      resetTimeout: 30000
    });
  }

  async getProductDetails(productId: string): Promise<ProductViewModel> {
    // Core functionality - must succeed for the page to work
    const product = await this.productApi.getProduct(productId);
    if (!product) {
      throw new Error(`Product ${productId} not found`);
    }

    // Build base response with defaults for degradable parts
    const result: ProductViewModel = {
      product,
      recommendations: [],
      reviews: [],
      serviceStatus: {
        recommendations: true,
        reviews: true
      }
    };

    // Get recommendations - non-critical, can fail gracefully
    try {
      result.recommendations = await this.circuitBreaker.execute(
        'recommendations',
        () => this.recommendationApi.getRecommendations(productId)
      );
    } catch (error) {
      console.warn(`Recommendations unavailable for product ${productId}:`, error);
      result.serviceStatus.recommendations = false;
    }

    // Get reviews - non-critical, can fail gracefully
    try {
      result.reviews = await this.circuitBreaker.execute(
        'reviews',
        () => this.reviewsApi.getReviews(productId)
      );
    } catch (error) {
      console.warn(`Reviews unavailable for product ${productId}:`, error);
      result.serviceStatus.reviews = false;
    }

    return result;
  }
}

// Frontend component that handles graceful degradation in the UI
class ProductDetailPage extends Component {
  state = {
    loading: true,
    product: null,
    recommendations: [],
    reviews: [],
    serviceStatus: {}
  };

  async componentDidMount() {
    try {
      const data = await productService.getProductDetails(this.props.productId);
      this.setState({
        loading: false,
        ...data
      });
    } catch (error) {
      this.setState({
        loading: false,
        error: "Unable to load product"
      });
    }
  }

  render() {
    const { loading, product, recommendations, reviews, serviceStatus, error } = this.state;

    if (loading) return <LoadingSpinner />;
    if (error) return <ErrorMessage message={error} />;

    return (
      <div className="product-page">
        {/* Core product information - always shown */}
        <ProductInfo product={product} />
        
        {/* Degradable feature: recommendations */}
        {serviceStatus.recommendations ? (
          <RecommendationSection products={recommendations} />
        ) : (
          <div className="degraded-notice">
            Product recommendations are currently unavailable
          </div>
        )}
        
        {/* Degradable feature: reviews */}
        {serviceStatus.reviews ? (
          <ReviewSection reviews={reviews} />
        ) : (
          <div className="degraded-notice">
            Customer reviews are temporarily unavailable
          </div>
        )}
      </div>
    );
  }
}
```

## When to Use

- For systems with multiple dependencies where partial functionality is better than none
- In user-facing applications where availability is critical
- When services have different levels of criticality
- In systems with components that have different availability SLAs

## When Not to Use

- In systems where all functions are equally critical
- When data consistency is more important than availability
- For backend batch processing systems where partial completion could create inconsistencies

## Related Patterns

- [Fallback Pattern](06-fallback-pattern.md)
- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Bulkhead Pattern](05-bulkhead-pattern.md)
