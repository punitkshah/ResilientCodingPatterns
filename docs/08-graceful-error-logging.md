# Graceful Error Logging & Propagation

## Overview

Effective error handling is crucial for building resilient cloud applications. The Graceful Error Logging & Propagation pattern focuses on consistently capturing, logging, and propagating errors across service boundaries while providing appropriate feedback to users and support teams.

## Problem Statement

In distributed systems, errors can occur at various levels and propagate through multiple services. Without a strategic approach to error handling, applications may:
- Leak sensitive information through error messages
- Fail to provide actionable information for troubleshooting
- Create poor user experiences due to cryptic error messages
- Miss critical errors due to inconsistent logging
- Struggle to correlate errors across service boundaries

## Solution: Graceful Error Logging & Propagation

Implement a comprehensive error handling strategy that:
1. **Captures** detailed error information at the source
2. **Logs** errors consistently with appropriate context and severity
3. **Transforms** technical exceptions into user-friendly messages
4. **Propagates** error context across service boundaries
5. **Centralizes** error monitoring for quick detection and resolution

## Implementation Strategies

### 1. Structured Error Logging

**Why**: Structured logs are easier to query, filter, and analyze than unstructured text.

**Guidelines**:
- Use structured logging frameworks that support property-based logging
- Include standard properties across all error logs
- Capture context details like correlation IDs, user information, and operation details
- Use appropriate log levels (Warning, Error, Critical) based on severity

**C# Example using Serilog**:

```csharp
// Configure structured logging in Program.cs
public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "OrderProcessingService")
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .WriteTo.Console(new JsonFormatter())
            .WriteTo.ApplicationInsights(
                services.GetRequiredService<TelemetryConfiguration>(),
                TelemetryConverter.Traces));
```

**Error Logging Service**:

```csharp
public class ErrorLogger
{
    private readonly ILogger<ErrorLogger> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ErrorLogger(
        ILogger<ErrorLogger> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public void LogError(Exception ex, string message, params object[] args)
    {
        // Get request context
        var httpContext = _httpContextAccessor.HttpContext;
        var requestId = httpContext?.TraceIdentifier;
        var userId = httpContext?.User?.Identity?.Name ?? "anonymous";
        var requestPath = httpContext?.Request?.Path.Value;
        
        // Create structured log with context properties
        using (LogContext.PushProperty("RequestId", requestId))
        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("RequestPath", requestPath))
        using (LogContext.PushProperty("ExceptionType", ex.GetType().Name))
        using (LogContext.PushProperty("StackTrace", ex.StackTrace))
        {
            _logger.LogError(ex, message, args);
        }
    }
}
```

### 2. Exception Middleware

**Why**: Centralized exception handling provides consistency and reduces duplication.

**Guidelines**:
- Implement middleware to catch and process unhandled exceptions
- Return standardized error responses that don't expose implementation details
- Include correlation IDs in responses for support reference
- Differentiate between client errors (4xx) and server errors (5xx)

**ASP.NET Core Example**:

```csharp
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var errorId = Guid.NewGuid().ToString();
        var requestId = context.TraceIdentifier;

        // Log the exception with context
        _logger.LogError(exception, 
            "Request {RequestId} failed with error {ErrorId}: {ErrorMessage}", 
            requestId, errorId, exception.Message);

        // Create an appropriate error response
        var statusCode = GetStatusCode(exception);
        var response = new ErrorResponse
        {
            ErrorId = errorId,
            RequestId = requestId,
            Status = statusCode,
            Title = GetTitle(exception),
            Detail = _environment.IsDevelopment() ? exception.Message : "An error occurred.",
            Type = "https://example.com/errors/" + statusCode
        };

        // Add stack trace in development
        if (_environment.IsDevelopment())
        {
            response.DeveloperDetails = new DeveloperErrorDetails
            {
                ExceptionType = exception.GetType().Name,
                StackTrace = exception.StackTrace?.Split(Environment.NewLine)
            };
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        return context.Response.WriteAsJsonAsync(response);
    }

    private int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            BadRequestException => StatusCodes.Status400BadRequest,
            NotFoundException => StatusCodes.Status404NotFound,
            ValidationException => StatusCodes.Status422UnprocessableEntity,
            ForbiddenException => StatusCodes.Status403Forbidden,
            UnauthorizedException => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private string GetTitle(Exception exception)
    {
        return exception switch
        {
            BadRequestException => "Bad Request",
            NotFoundException => "Resource Not Found",
            ValidationException => "Validation Error",
            ForbiddenException => "Forbidden",
            UnauthorizedException => "Unauthorized",
            _ => "Server Error"
        };
    }
}

// Register in Startup.cs
public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // Use the custom exception handler
    app.UseMiddleware<GlobalExceptionMiddleware>();
    
    // Other middleware
}
```

**Error Response Model**:

```csharp
// Based on RFC 7807 - Problem Details for HTTP APIs
public class ErrorResponse
{
    public string ErrorId { get; set; }
    public string RequestId { get; set; }
    public string Type { get; set; }
    public string Title { get; set; }
    public int Status { get; set; }
    public string Detail { get; set; }
    public DeveloperErrorDetails DeveloperDetails { get; set; }
}

public class DeveloperErrorDetails
{
    public string ExceptionType { get; set; }
    public string[] StackTrace { get; set; }
}
```

### 3. Custom Exception Types

**Why**: Domain-specific exceptions improve error clarity and handling precision.

**Guidelines**:
- Create custom exceptions that represent business rules and domains
- Include relevant context information in exception properties
- Design exceptions to support proper HTTP status code mapping
- Avoid exposing implementation details in exception messages

**Custom Exceptions Example**:

```csharp
// Base exception for application-specific errors
public abstract class ApplicationException : Exception
{
    public string ErrorCode { get; }

    protected ApplicationException(string message, string errorCode = null) 
        : base(message)
    {
        ErrorCode = errorCode ?? GetType().Name;
    }

    protected ApplicationException(string message, Exception innerException, string errorCode = null) 
        : base(message, innerException)
    {
        ErrorCode = errorCode ?? GetType().Name;
    }
}

// Domain-specific exceptions
public class NotFoundException : ApplicationException
{
    public string ResourceType { get; }
    public string ResourceId { get; }

    public NotFoundException(string resourceType, string resourceId) 
        : base($"{resourceType} with ID '{resourceId}' was not found")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }
}

public class ValidationException : ApplicationException
{
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    public ValidationException(IDictionary<string, string[]> validationErrors) 
        : base("One or more validation errors occurred")
    {
        ValidationErrors = new ReadOnlyDictionary<string, string[]>(
            validationErrors ?? new Dictionary<string, string[]>());
    }
}

public class BusinessRuleException : ApplicationException
{
    public BusinessRuleException(string message, string errorCode = null) 
        : base(message, errorCode)
    {
    }
}
```

### 4. Distributed Tracing and Correlation

**Why**: In microservices, errors often span multiple services, making correlation essential.

**Guidelines**:
- Pass correlation IDs through all service calls
- Implement consistent request ID generation and propagation
- Use distributed tracing tools like Application Insights or OpenTelemetry
- Ensure logging includes correlation context in all services

**Correlation Middleware Example**:

```csharp
public class CorrelationMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);
        
        // Add correlation ID to the response
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
            {
                context.Response.Headers.Add(CorrelationIdHeader, correlationId);
            }
            return Task.CompletedTask;
        });

        // Store correlation ID in the logging context
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId) &&
            !string.IsNullOrEmpty(correlationId))
        {
            return correlationId;
        }

        return Guid.NewGuid().ToString();
    }
}

// HTTP client that propagates correlation IDs
public class CorrelatingHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _contextAccessor;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelatingHttpClient(
        HttpClient httpClient,
        IHttpContextAccessor contextAccessor)
    {
        _httpClient = httpClient;
        _contextAccessor = contextAccessor;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        // Get current correlation ID
        var correlationId = _contextAccessor.HttpContext?.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        // Add correlation ID to the outgoing request
        request.Headers.Add(CorrelationIdHeader, correlationId);

        return await _httpClient.SendAsync(request);
    }
}
```

**OpenTelemetry Integration**:

```csharp
public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            // Configure OpenTelemetry
            services.AddOpenTelemetryTracing((builder) => builder
                .SetResourceBuilder(ResourceBuilder
                    .CreateDefault()
                    .AddService(serviceName: "OrderService", serviceVersion: "1.0.0"))
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddAzureMonitorTraceExporter(options =>
                {
                    options.ConnectionString = context.Configuration["ApplicationInsights:ConnectionString"];
                }));
        });
```

### 5. Graceful Degradation with Error Handling

**Why**: Resilient applications must handle errors without complete service failure.

**Guidelines**:
- Design fallback mechanisms for critical operations
- Return partial data when complete data is unavailable
- Use circuit breakers to prevent cascading failures
- Implement feature flags to disable problematic features

**Service with Fallback Example**:

```csharp
public class ProductService
{
    private readonly IProductRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProductService> _logger;
    private readonly CircuitBreakerPolicy _circuitBreaker;

    public ProductService(
        IProductRepository repository,
        IMemoryCache cache,
        ILogger<ProductService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
        
        // Create circuit breaker
        _circuitBreaker = Policy
            .Handle<Exception>(ex => !(ex is ValidationException)) // Don't handle validation errors
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: (ex, breakDuration) =>
                {
                    _logger.LogError(ex, 
                        "Circuit breaker opened for {BreakDuration}. Repository access suspended.", 
                        breakDuration);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset. Repository access resumed.");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker half-open. Testing repository access.");
                });
    }

    public async Task<ProductResponse> GetProductByIdAsync(string productId)
    {
        try
        {
            // Try the primary data source with circuit breaker
            return await _circuitBreaker.ExecuteAsync(async () =>
            {
                var product = await _repository.GetProductByIdAsync(productId);
                
                if (product == null)
                {
                    throw new NotFoundException("Product", productId);
                }
                
                // Cache the successful result for fallback
                _cache.Set($"product:{productId}", product, TimeSpan.FromHours(1));
                
                return new ProductResponse
                {
                    Id = product.Id,
                    Name = product.Name,
                    Price = product.Price,
                    Description = product.Description,
                    IsAvailable = product.StockQuantity > 0,
                    IsDataFresh = true
                };
            });
        }
        catch (Exception ex)
        {
            // Log the error
            _logger.LogError(ex, "Error retrieving product {ProductId}", productId);
            
            // Try fallback to cache
            if (_cache.TryGetValue($"product:{productId}", out Product cachedProduct))
            {
                _logger.LogInformation("Returning cached product data for {ProductId}", productId);
                
                return new ProductResponse
                {
                    Id = cachedProduct.Id,
                    Name = cachedProduct.Name,
                    Price = cachedProduct.Price,
                    Description = cachedProduct.Description,
                    IsAvailable = cachedProduct.StockQuantity > 0,
                    IsDataFresh = false,
                    LastUpdated = cachedProduct.LastUpdated
                };
            }
            
            // If it's a NotFoundException, propagate it
            if (ex is NotFoundException)
            {
                throw;
            }
            
            // For other errors, return minimal emergency response
            return new ProductResponse
            {
                Id = productId,
                Name = "Product information temporarily unavailable",
                IsDataFresh = false,
                IsError = true,
                ErrorMessage = "We're experiencing technical difficulties. Please try again later."
            };
        }
    }
}
```

## TypeScript Implementation Example

```typescript
import { Request, Response, NextFunction } from 'express';
import { v4 as uuidv4 } from 'uuid';
import { Logger } from './logger';

// Error response model (RFC 7807 - Problem Details)
interface ErrorResponse {
  errorId: string;
  requestId: string;
  type: string;
  title: string;
  status: number;
  detail: string;
  developerDetails?: {
    exceptionType: string;
    stackTrace: string[];
  };
}

// Base application error
export class ApplicationError extends Error {
  readonly statusCode: number;
  readonly errorCode: string;

  constructor(message: string, statusCode: number = 500, errorCode?: string) {
    super(message);
    this.name = this.constructor.name;
    this.statusCode = statusCode;
    this.errorCode = errorCode || this.constructor.name;
    Error.captureStackTrace(this, this.constructor);
  }
}

// Domain-specific errors
export class NotFoundError extends ApplicationError {
  readonly resourceType: string;
  readonly resourceId: string;

  constructor(resourceType: string, resourceId: string) {
    super(`${resourceType} with ID '${resourceId}' was not found`, 404);
    this.resourceType = resourceType;
    this.resourceId = resourceId;
  }
}

export class ValidationError extends ApplicationError {
  readonly validationErrors: Record<string, string[]>;

  constructor(validationErrors: Record<string, string[]>) {
    super('One or more validation errors occurred', 422);
    this.validationErrors = validationErrors;
  }
}

// Error logging service
export class ErrorLogger {
  constructor(private logger: Logger) {}

  logError(error: Error, request?: Request, context?: Record<string, any>): void {
    const errorContext = {
      requestId: request?.headers['x-request-id'] || uuidv4(),
      userId: request?.user?.id || 'anonymous',
      path: request?.path,
      method: request?.method,
      exceptionType: error.constructor.name,
      ...context
    };

    if (error instanceof ApplicationError) {
      errorContext['errorCode'] = error.errorCode;
      errorContext['statusCode'] = error.statusCode;
    }

    this.logger.error(
      `[${errorContext.requestId}] ${error.message}`, 
      { 
        error: {
          message: error.message,
          stack: error.stack,
          ...errorContext
        }
      }
    );
  }
}

// Global error handling middleware for Express
export function errorHandlerMiddleware(
  logger: ErrorLogger,
  isDevelopment: boolean = process.env.NODE_ENV === 'development'
) {
  return (error: Error, req: Request, res: Response, next: NextFunction): void => {
    const errorId = uuidv4();
    const requestId = req.headers['x-request-id'] as string || req.headers['x-correlation-id'] as string || uuidv4();

    // Log the error
    logger.logError(error, req, { errorId });

    // Determine status code
    const statusCode = error instanceof ApplicationError 
      ? error.statusCode 
      : 500;

    // Create error response
    const errorResponse: ErrorResponse = {
      errorId,
      requestId,
      type: `https://example.com/errors/${statusCode}`,
      title: getErrorTitle(error),
      status: statusCode,
      detail: isDevelopment ? error.message : 'An error occurred.'
    };

    // Add developer details in development mode
    if (isDevelopment) {
      errorResponse.developerDetails = {
        exceptionType: error.constructor.name,
        stackTrace: error.stack?.split('\n') || []
      };
    }

    // Add validation errors if present
    if (error instanceof ValidationError) {
      Object.assign(errorResponse, { validationErrors: error.validationErrors });
    }

    res.status(statusCode).json(errorResponse);
  };
}

// Helper to get human-friendly error titles
function getErrorTitle(error: Error): string {
  if (error instanceof NotFoundError) return 'Resource Not Found';
  if (error instanceof ValidationError) return 'Validation Error';
  if (error instanceof ApplicationError && error.statusCode === 403) return 'Forbidden';
  if (error instanceof ApplicationError && error.statusCode === 401) return 'Unauthorized';
  if (error instanceof ApplicationError && error.statusCode === 400) return 'Bad Request';
  return 'Server Error';
}

// Correlation middleware for Express
export function correlationMiddleware(req: Request, res: Response, next: NextFunction): void {
  // Get or create correlation ID
  const correlationHeaderName = 'x-correlation-id';
  const correlationId = req.headers[correlationHeaderName] as string || uuidv4();
  
  // Add to response headers
  res.setHeader(correlationHeaderName, correlationId);
  
  // Add to request for other middleware/handlers to access
  req.headers[correlationHeaderName] = correlationId;
  
  next();
}

// HTTP client with correlation ID propagation
export class CorrelatingHttpClient {
  constructor(private baseUrl: string) {}

  async request<T>(
    method: string,
    path: string,
    options: { body?: any, headers?: Record<string, string>, correlationId?: string } = {}
  ): Promise<T> {
    const headers = { 
      'Content-Type': 'application/json',
      ...options.headers
    };
    
    // Add correlation ID if provided
    if (options.correlationId) {
      headers['x-correlation-id'] = options.correlationId;
    }
    
    const response = await fetch(`${this.baseUrl}${path}`, {
      method,
      headers,
      body: options.body ? JSON.stringify(options.body) : undefined
    });
    
    if (!response.ok) {
      const errorData = await response.json().catch(() => null);
      throw new ApplicationError(
        errorData?.detail || `Request failed with status: ${response.status}`,
        response.status
      );
    }
    
    return await response.json() as T;
  }
}
```

## Azure Application Insights Integration

For applications running on Azure, Application Insights provides comprehensive error monitoring and distributed tracing capabilities.

**ASP.NET Core Integration**:

```csharp
public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) =>
        {
            // Configuration setup
        })
        .ConfigureServices((context, services) =>
        {
            // Add Application Insights with adaptive sampling
            services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = context.Configuration["ApplicationInsights:ConnectionString"];
                options.EnableAdaptiveSampling = true;
                options.EnableQuickPulseMetricStream = true;
            });
            
            // Configure telemetry initializer for custom properties
            services.AddSingleton<ITelemetryInitializer, CustomTelemetryInitializer>();
            
            // Add other services
        });
```

**Custom Telemetry Initializer**:

```csharp
public class CustomTelemetryInitializer : ITelemetryInitializer
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostEnvironment _environment;

    public CustomTelemetryInitializer(
        IHttpContextAccessor httpContextAccessor,
        IHostEnvironment environment)
    {
        _httpContextAccessor = httpContextAccessor;
        _environment = environment;
    }

    public void Initialize(ITelemetry telemetry)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        
        // Add application context
        telemetry.Context.Cloud.RoleName = "OrderService";
        telemetry.Context.Component.Version = "1.0.0";
        telemetry.Context.GlobalProperties["Environment"] = _environment.EnvironmentName;
        
        if (httpContext == null) return;
        
        // Add user and session details when available
        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            telemetry.Context.User.Id = httpContext.User.FindFirst("sub")?.Value;
            telemetry.Context.User.AuthenticatedUserId = httpContext.User.Identity.Name;
        }
        
        // Add correlation IDs
        if (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            telemetry.Context.Operation.Id = correlationId;
        }
    }
}
```

## When to Use

- In all production applications, especially distributed systems
- When building microservices architectures
- For applications with critical reliability requirements
- In systems where operational visibility is essential
- When implementing comprehensive observability

## When Not to Use

- For simple prototype applications
- When excessive logging would impact performance in time-critical operations
- In extremely resource-constrained environments

## Related Patterns

- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Retry Pattern](02-retry-pattern.md)
- [Fallback Pattern](fallback-mechanism.md)
- [Bulkhead Pattern](05-bulkhead-pattern.md)

## Next Steps

Continue to the [Fallback Mechanism](fallback-mechanism.md) pattern to learn how to implement alternative behavior when a service fails.
