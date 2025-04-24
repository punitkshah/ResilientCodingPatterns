# Chaos Engineering Practices

## Overview

Chaos Engineering is the discipline of experimenting on a system to build confidence in its ability to withstand turbulent conditions in production. By deliberately introducing failures in a controlled environment, teams can identify weaknesses and improve the system's resilience before actual failures occur in production.

## Problem Statement

Cloud-based systems have numerous potential failure points that are often discovered only when they fail in production, causing real impact to users. Traditional testing doesn't adequately simulate real-world failure scenarios, leaving systems vulnerable to unknown weaknesses.

## Solution: Chaos Engineering

Implement controlled experiments that inject failures or adverse conditions into your system to test its resilience. These experiments follow a scientific approach:

1. **Define steady state**: Determine what constitutes normal behavior
2. **Form hypothesis**: Predict how the system will respond to a specific failure
3. **Introduce failure**: Simulate a real-world failure scenario
4. **Observe results**: Measure and analyze system behavior
5. **Improve resilience**: Address weaknesses discovered during the experiment

## Benefits

- Builds confidence in system behavior during failures
- Identifies unknown weaknesses before they affect users
- Validates recovery mechanisms and resilience patterns
- Creates a culture of resilience within engineering teams
- Improves system documentation and understanding

## Implementation Considerations

- Start with small, controlled experiments in non-production environments
- Ensure you have adequate monitoring and observability in place
- Define clear abort conditions to prevent unintended damage
- Gradually increase experiment complexity and scope
- Include business stakeholders to build organizational resilience thinking

## Code Example: Chaos Engineering in C#

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

// Middleware for injecting chaos into your ASP.NET Core application
public class ChaosMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ChaosOptions _options;
    private readonly ILogger<ChaosMiddleware> _logger;
    private readonly Random _random = new Random();

    public ChaosMiddleware(
        RequestDelegate next,
        ChaosOptions options,
        ILogger<ChaosMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bypass chaos if not enabled or if the request path is excluded
        if (!_options.Enabled || IsExcludedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Introduce chaos based on probability settings
        if (ShouldInjectLatency())
        {
            await InjectLatency();
        }
        
        if (ShouldInjectException())
        {
            InjectException();
            return; // Don't continue processing if we're injecting an exception
        }
        
        if (ShouldInjectStatusCode())
        {
            InjectStatusCode(context);
            return; // Don't continue processing if we're returning a status code
        }

        // Proceed with the request normally if no chaos was injected
        await _next(context);
    }

    private bool IsExcludedPath(string path)
    {
        foreach (var excludedPath in _options.ExcludedPaths)
        {
            if (path.StartsWith(excludedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private bool ShouldInjectLatency()
    {
        return _random.NextDouble() < _options.LatencyProbability;
    }

    private async Task InjectLatency()
    {
        var latencyMs = _random.Next(
            _options.MinLatencyMs,
            _options.MaxLatencyMs);
            
        _logger.LogInformation($"Chaos: Injecting {latencyMs}ms latency");
        await Task.Delay(latencyMs);
    }

    private bool ShouldInjectException()
    {
        return _random.NextDouble() < _options.ExceptionProbability;
    }

    private void InjectException()
    {
        _logger.LogInformation("Chaos: Injecting exception");
        throw new ChaosEngineedException("This exception was injected by the chaos middleware");
    }

    private bool ShouldInjectStatusCode()
    {
        return _random.NextDouble() < _options.StatusCodeProbability;
    }

    private void InjectStatusCode(HttpContext context)
    {
        var statusCodes = new[] { 429, 500, 503 };
        var statusCode = statusCodes[_random.Next(statusCodes.Length)];
        
        _logger.LogInformation($"Chaos: Injecting status code {statusCode}");
        context.Response.StatusCode = statusCode;
    }
}

// Configuration options for the chaos middleware
public class ChaosOptions
{
    public bool Enabled { get; set; } = false;
    public double LatencyProbability { get; set; } = 0.0;
    public int MinLatencyMs { get; set; } = 200;
    public int MaxLatencyMs { get; set; } = 5000;
    public double ExceptionProbability { get; set; } = 0.0;
    public double StatusCodeProbability { get; set; } = 0.0;
    public string[] ExcludedPaths { get; set; } = new[] { "/health", "/metrics" };
}

// Custom exception for chaos engineering
public class ChaosEngineedException : Exception
{
    public ChaosEngineedException(string message) : base(message) { }
}

// Extension method to register the chaos middleware
public static class ChaosMiddlewareExtensions
{
    public static IApplicationBuilder UseChaos(
        this IApplicationBuilder builder,
        ChaosOptions options)
    {
        return builder.UseMiddleware<ChaosMiddleware>(options);
    }
}

// Example of using the middleware in a web application
public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // Other middleware registration
    
    // Only enable chaos in specific environments
    if (env.IsEnvironment("Chaos") || env.IsEnvironment("Staging"))
    {
        app.UseChaos(new ChaosOptions
        {
            Enabled = true,
            LatencyProbability = 0.1,   // 10% of requests will experience latency
            ExceptionProbability = 0.05, // 5% of requests will throw exceptions
            StatusCodeProbability = 0.05 // 5% of requests will return error status codes
        });
    }
    
    // Rest of the middleware pipeline
}
```

## Code Example: Chaos Engineering in TypeScript

```typescript
// Chaos Monkey for Express applications

import express, { Request, Response, NextFunction } from 'express';
import winston from 'winston';

interface ChaosOptions {
  enabled: boolean;
  latencyProbability: number;
  minLatencyMs: number;
  maxLatencyMs: number;
  exceptionProbability: number;
  statusCodeProbability: number;
  excludedPaths: string[];
}

class ChaosMonkey {
  private options: ChaosOptions;
  private logger: winston.Logger;

  constructor(options: Partial<ChaosOptions> = {}) {
    this.options = {
      enabled: options.enabled ?? false,
      latencyProbability: options.latencyProbability ?? 0,
      minLatencyMs: options.minLatencyMs ?? 200,
      maxLatencyMs: options.maxLatencyMs ?? 5000,
      exceptionProbability: options.exceptionProbability ?? 0,
      statusCodeProbability: options.statusCodeProbability ?? 0,
      excludedPaths: options.excludedPaths ?? ['/health', '/metrics'],
    };

    this.logger = winston.createLogger({
      level: 'info',
      format: winston.format.combine(
        winston.format.timestamp(),
        winston.format.json()
      ),
      defaultMeta: { service: 'chaos-monkey' },
      transports: [
        new winston.transports.Console(),
      ],
    });
  }

  middleware() {
    return (req: Request, res: Response, next: NextFunction) => {
      if (!this.options.enabled || this.isExcludedPath(req.path)) {
        return next();
      }

      if (this.shouldInjectLatency()) {
        const latency = this.getRandomLatency();
        this.logger.info(`Injecting latency: ${latency}ms`);
        
        setTimeout(() => {
          // After latency, continue with other chaos checks
          this.processOtherChaos(req, res, next);
        }, latency);
      } else {
        this.processOtherChaos(req, res, next);
      }
    };
  }

  private processOtherChaos(req: Request, res: Response, next: NextFunction) {
    if (this.shouldInjectException()) {
      this.logger.info('Injecting exception');
      throw new Error('Chaos Monkey exception');
    }

    if (this.shouldInjectStatusCode()) {
      const statusCode = this.getRandomErrorStatusCode();
      this.logger.info(`Injecting status code: ${statusCode}`);
      return res.status(statusCode).json({
        error: 'Chaos Monkey error',
        message: `Synthetic error response (${statusCode})`,
      });
    }

    // If we reach here, no chaos was injected, so continue normally
    next();
  }

  private isExcludedPath(path: string): boolean {
    return this.options.excludedPaths.some(excludedPath => 
      path.startsWith(excludedPath));
  }

  private shouldInjectLatency(): boolean {
    return Math.random() < this.options.latencyProbability;
  }

  private getRandomLatency(): number {
    return Math.floor(
      Math.random() * (this.options.maxLatencyMs - this.options.minLatencyMs + 1)
    ) + this.options.minLatencyMs;
  }

  private shouldInjectException(): boolean {
    return Math.random() < this.options.exceptionProbability;
  }

  private shouldInjectStatusCode(): boolean {
    return Math.random() < this.options.statusCodeProbability;
  }

  private getRandomErrorStatusCode(): number {
    const statusCodes = [429, 500, 503];
    return statusCodes[Math.floor(Math.random() * statusCodes.length)];
  }
}

// Example usage in an Express application
const app = express();

// Create the chaos monkey and attach its middleware
const chaosMonkey = new ChaosMonkey({
  enabled: process.env.ENABLE_CHAOS === 'true',
  latencyProbability: 0.1,
  exceptionProbability: 0.05,
  statusCodeProbability: 0.05,
  excludedPaths: ['/health', '/metrics', '/favicon.ico'],
});

// Insert the chaos monkey before your routes
app.use(chaosMonkey.middleware());

// Your routes
app.get('/', (req, res) => {
  res.send('Hello World!');
});

// Error handler middleware (must be defined after all routes)
app.use((err: Error, req: Request, res: Response, next: NextFunction) => {
  console.error(err);
  res.status(500).json({ error: 'Internal Server Error', message: err.message });
});

// Start server
app.listen(3000, () => {
  console.log('Server listening on port 3000');
});
```

## When to Use

- In pre-production environments to validate system resilience
- After implementing resilience patterns to verify they work as expected
- As part of a continuous resilience testing strategy
- When preparing for high-traffic events or launches
- For systems with critical reliability requirements

## When Not to Use

- In production without proper safeguards and controlled blast radius
- Without adequate monitoring and observability in place
- In systems that haven't implemented basic resilience patterns
- When the team doesn't have procedures for handling real incidents

## Related Patterns

- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Retry Pattern](02-retry-pattern.md)
- [Bulkhead Pattern](05-bulkhead-pattern.md)
