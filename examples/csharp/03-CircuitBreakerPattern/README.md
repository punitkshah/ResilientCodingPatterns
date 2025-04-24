# Circuit Breaker Pattern

This example demonstrates the implementation of the Circuit Breaker pattern in C#.

## Overview

The Circuit Breaker pattern prevents an application from repeatedly trying to execute an operation that's likely to fail, allowing it to continue without waiting for the fault to be fixed or wasting CPU cycles. It's like an electrical circuit breaker that stops the flow of current when a fault is detected.

## Key Concepts

- **Closed State**: Normal operation, requests pass through
- **Open State**: Circuit is broken, requests fail immediately without calling the service
- **Half-Open State**: After a timeout, the circuit allows a limited number of test requests to determine if the service is healthy again

## Implementation Details

This example uses Polly's CircuitBreaker policy to implement the pattern. It includes:

1. Basic circuit breaker implementation with HttpClient
2. Advanced circuit breaker with fallback mechanism

## Running the Example

```
dotnet run
```

## Dependencies

- .NET 6.0
- Polly
- Microsoft.Extensions.Http.Polly

## Additional Resources

- [Circuit Breaker Pattern - Microsoft docs](https://docs.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- [Polly Documentation](https://github.com/App-vNext/Polly)
