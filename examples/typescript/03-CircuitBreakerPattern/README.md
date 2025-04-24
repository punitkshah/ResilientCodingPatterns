# Circuit Breaker Pattern - TypeScript Implementation

This example demonstrates the implementation of the Circuit Breaker pattern in TypeScript.

## Overview

The Circuit Breaker pattern prevents an application from repeatedly trying to execute an operation that's likely to fail, allowing it to continue without waiting for the fault to be fixed or wasting resources. It functions like an electrical circuit breaker that stops the flow of current when a fault is detected.

## Key Concepts

- **Closed State**: Normal operation, requests pass through
- **Open State**: Circuit is broken, requests fail immediately without calling the service
- **Half-Open State**: After a timeout, the circuit allows a limited number of test requests to determine if the service is healthy again

## Implementation Details

This example uses the 'opossum' library to implement the Circuit Breaker pattern. It demonstrates:

1. Basic circuit breaker implementation with HTTP requests
2. State transitions (open, half-open, closed)
3. Fallback mechanism for when the circuit is open

## Running the Example

```bash
# Install dependencies
npm install

# Run the example
npm start
```

## Dependencies

- axios - For making HTTP requests
- opossum - Circuit breaker implementation
- TypeScript and ts-node

## Additional Resources

- [Circuit Breaker Pattern - Microsoft docs](https://docs.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- [Opossum Documentation](https://nodeshift.dev/opossum/)
