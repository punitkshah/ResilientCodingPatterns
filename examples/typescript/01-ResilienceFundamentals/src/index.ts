import axios, { AxiosError } from 'axios';

console.log('Resilience Fundamentals Examples');
console.log('===============================\n');

// Example 1: Basic Error Handling
function basicErrorHandlingExample(): void {
  console.log('Example 1: Basic Error Handling');
  console.log('------------------------------');

  try {
    // Simulate an operation that may fail
    simulateOperation(true);
    console.log('Operation completed successfully.');
  } catch (error) {
    if (error instanceof Error) {
      if (error.name === 'InvalidOperationError') {
        console.log(`Caught specific error: ${error.message}`);
        // Handle specific error type with appropriate recovery logic
      } else {
        console.log(`Caught general error: ${error.message}`);
        // Generic fallback handling
      }
    } else {
      console.log('Caught unknown error type');
    }
  } finally {
    console.log('Cleanup operations in finally block');
    // Resource cleanup, logging, etc.
  }
}

function simulateOperation(shouldFail: boolean): void {
  console.log('Executing operation...');
  
  if (shouldFail) {
    const error = new Error('Operation failed due to business rule violation');
    error.name = 'InvalidOperationError';
    throw error;
  }
  
  // Operation succeeds if we get here
}

// Example 2: Asynchronous Error Handling with Timeouts
async function asyncErrorHandlingExample(): Promise<void> {
  console.log('\nExample 2: Asynchronous Error Handling');
  console.log('-------------------------------------');

  // Create an AbortController for timeout management
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 2000); // 2 second timeout
  
  try {
    await simulateAsyncOperation(controller.signal);
    console.log('Async operation completed successfully');
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      console.log('Operation was cancelled or timed out');
    } else if (error instanceof Error) {
      console.log(`Async operation failed: ${error.message}`);
    } else {
      console.log('Unknown error occurred');
    }
  } finally {
    clearTimeout(timeoutId); // Clean up the timeout
  }
}

async function simulateAsyncOperation(signal: AbortSignal): Promise<void> {
  console.log('Starting asynchronous operation...');
  
  // Register abort event handler
  signal.addEventListener('abort', () => {
    console.log('Abort was requested');
  });
  
  for (let i = 0; i < 5; i++) {
    // Check for cancellation before each operation
    if (signal.aborted) {
      throw new DOMException('Operation aborted', 'AbortError');
    }
    
    console.log(`Async operation step ${i+1}`);
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  
  console.log('Asynchronous operation completed');
}

// Example 3: Health Monitoring
async function healthMonitoringExample(): Promise<void> {
  console.log('\nExample 3: Health Monitoring');
  console.log('---------------------------');

  const healthMonitor = new HealthMonitor();
  
  // Register components to monitor
  healthMonitor.registerComponent('Database', checkDatabaseHealth);
  healthMonitor.registerComponent('API Service', checkApiHealth);
  healthMonitor.registerComponent('Cache', checkCacheHealth);
  
  // Run health check
  await healthMonitor.checkAllComponentsAsync();
}

async function checkDatabaseHealth(): Promise<boolean> {
  console.log('Checking database connection...');
  await new Promise(resolve => setTimeout(resolve, 300)); // Simulate actual health check
  const isHealthy = Math.random() * 100 > 10; // 90% chance of being healthy
  console.log(`Database health: ${isHealthy ? 'Healthy' : 'Unhealthy'}`);
  return isHealthy;
}

async function checkApiHealth(): Promise<boolean> {
  console.log('Checking API service...');
  await new Promise(resolve => setTimeout(resolve, 200)); // Simulate actual health check
  const isHealthy = Math.random() * 100 > 20; // 80% chance of being healthy
  console.log(`API health: ${isHealthy ? 'Healthy' : 'Unhealthy'}`);
  return isHealthy;
}

async function checkCacheHealth(): Promise<boolean> {
  console.log('Checking cache service...');
  await new Promise(resolve => setTimeout(resolve, 100)); // Simulate actual health check
  const isHealthy = Math.random() * 100 > 5; // 95% chance of being healthy
  console.log(`Cache health: ${isHealthy ? 'Healthy' : 'Unhealthy'}`);
  return isHealthy;
}

class HealthMonitor {
  private components: Map<string, () => Promise<boolean>> = new Map();

  registerComponent(name: string, healthCheck: () => Promise<boolean>): void {
    this.components.set(name, healthCheck);
  }

  async checkAllComponentsAsync(): Promise<void> {
    console.log('Running health checks for all components...');
    
    let healthyCount = 0;
    for (const [name, checkFn] of this.components.entries()) {
      const isHealthy = await checkFn();
      if (isHealthy) healthyCount++;
    }
    
    const overallHealth = healthyCount / this.components.size;
    console.log(`Overall system health: ${(overallHealth * 100).toFixed(0)}%`);
    
    if (overallHealth < 0.5) {
      console.log('ALERT: System is in critical state!');
    } else if (overallHealth < 0.8) {
      console.log('WARNING: Some system components are unhealthy');
    } else {
      console.log('System is healthy');
    }
  }
}

// Example 4: Graceful Degradation
async function gracefulDegradationExample(): Promise<void> {
  console.log('\nExample 4: Graceful Degradation');
  console.log('-------------------------------');

  const service = new ServiceWithFallback();
  
  // Try to get data from primary source
  const result = await service.getDataAsync();
  console.log(`Final result: ${result}`);
}

class ServiceWithFallback {
  async getDataAsync(): Promise<string> {
    try {
      console.log('Attempting to retrieve data from primary source...');
      return await this.getDataFromPrimarySourceAsync();
    } catch (error) {
      console.log(`Primary source failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
      console.log('Falling back to secondary source...');
      
      try {
        return await this.getDataFromSecondarySourceAsync();
      } catch (secondaryError) {
        console.log(`Secondary source failed: ${secondaryError instanceof Error ? secondaryError.message : 'Unknown error'}`);
        console.log('Using cached data as last resort...');
        return this.getCachedData();
      }
    }
  }

  private async getDataFromPrimarySourceAsync(): Promise<string> {
    await new Promise(resolve => setTimeout(resolve, 300)); // Simulate network delay
    
    // Simulate 70% chance of failure
    if (Math.random() < 0.7) {
      throw new Error('Primary source connection timeout');
    }
    
    return 'Data from primary source (most up-to-date)';
  }

  private async getDataFromSecondarySourceAsync(): Promise<string> {
    await new Promise(resolve => setTimeout(resolve, 200)); // Simulate network delay
    
    // Simulate 30% chance of failure
    if (Math.random() < 0.3) {
      throw new Error('Secondary source unavailable');
    }
    
    return 'Data from secondary source (slightly delayed)';
  }

  private getCachedData(): string {
    // Cache always works, but might be stale
    return 'Cached data from last successful retrieval (may be stale)';
  }
}

// Run all examples
async function runAllExamples(): Promise<void> {
  // Example 1: Basic error handling
  basicErrorHandlingExample();
  
  // Example 2: Asynchronous error handling
  await asyncErrorHandlingExample();
  
  // Example 3: Health monitoring
  await healthMonitoringExample();
  
  // Example 4: Graceful degradation
  await gracefulDegradationExample();
  
  console.log('\nAll resilience fundamentals examples completed.');
}

// Execute all examples
runAllExamples().catch(error => {
  console.error('Error running examples:', error);
});
