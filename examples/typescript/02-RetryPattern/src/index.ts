import axios, { AxiosRequestConfig, AxiosError } from 'axios';
import axiosRetry from 'axios-retry';

console.log('Retry Pattern Examples');
console.log('=====================\n');

// Example 1: Basic retry using axios-retry
async function basicRetryExample() {
  console.log('Example 1: Basic Retry with axios-retry');
  console.log('--------------------------------------');

  // Configure axios with retry functionality
  const client = axios.create();
  axiosRetry(client, {
    retries: 3, // Number of retry attempts
    retryDelay: (retryCount) => {
      console.log(`Retry attempt: ${retryCount}`);
      return retryCount * 1000; // Time interval between retries (ms)
    },
    retryCondition: (error: AxiosError) => {
      // Retry on network errors and 5xx responses
      return axiosRetry.isNetworkOrIdempotentRequestError(error) || 
             (error.response?.status !== undefined && error.response?.status >= 500);
    }
  });

  try {
    console.log('Sending request to a non-existent endpoint (will fail)...');
    const response = await client.get('https://httpstat.us/500'); // Will return 500 status
    console.log('Response:', response.data);
  } catch (error) {
    console.log('All retry attempts failed:', error.message);
  }
}

// Example 2: Custom retry implementation with exponential backoff
async function customRetryExample() {
  console.log('\nExample 2: Custom Retry Implementation with Exponential Backoff');
  console.log('----------------------------------------------------------');

  // Generic retry function with exponential backoff
  async function retryOperation<T>(
    operation: () => Promise<T>, 
    retries: number = 3, 
    baseDelay: number = 1000,
    maxDelay: number = 10000
  ): Promise<T> {
    let lastError: any;
    
    for (let attempt = 0; attempt <= retries; attempt++) {
      try {
        if (attempt > 0) {
          // Calculate exponential backoff with jitter
          const delay = Math.min(
            Math.random() * baseDelay * Math.pow(2, attempt - 1),
            maxDelay
          );
          console.log(`Attempt ${attempt}: Waiting ${delay.toFixed(0)}ms before retrying...`);
          await new Promise(resolve => setTimeout(resolve, delay));
        }
        
        return await operation();
      } catch (error) {
        console.log(`Attempt ${attempt + 1} failed:`, error.message);
        lastError = error;
        
        // Check if we should retry based on error type
        if (!isRetryableError(error) || attempt >= retries) {
          break;
        }
      }
    }
    
    throw lastError || new Error('Operation failed after retries');
  }

  // Helper function to determine if an error is retryable
  function isRetryableError(error: any): boolean {
    // Network errors are always retryable
    if (error.code === 'ECONNREFUSED' || error.code === 'ECONNRESET' || error.code === 'ETIMEDOUT') {
      return true;
    }
    
    // Retry on server errors (5xx)
    if (error.response && error.response.status >= 500 && error.response.status < 600) {
      return true;
    }
    
    // Too Many Requests response
    if (error.response && error.response.status === 429) {
      return true;
    }
    
    return false;
  }

  // Example service that sometimes fails
  async function unreliableService(): Promise<string> {
    // Simulate an operation that sometimes succeeds and sometimes fails
    const random = Math.random();
    
    if (random < 0.7) {
      throw new Error(`Service unavailable (${random.toFixed(2)})`);
    }
    
    return `Operation succeeded (${random.toFixed(2)})`;
  }

  try {
    console.log('Calling unreliable service with retry logic...');
    const result = await retryOperation(unreliableService, 5);
    console.log('Final result:', result);
  } catch (error) {
    console.log('All retry attempts exhausted:', error.message);
  }
}

// Example 3: Retry with circuit breaker combination
async function retryWithCircuitBreakerExample() {
  console.log('\nExample 3: Retry with Circuit Breaker Combination');
  console.log('-----------------------------------------------');
  
  // Simple circuit breaker implementation
  class CircuitBreaker {
    private failures: number = 0;
    private state: 'CLOSED' | 'OPEN' = 'CLOSED';
    private nextAttempt: number = Date.now();
    
    constructor(
      private threshold: number = 3,
      private timeout: number = 5000
    ) {}
    
    async execute<T>(operation: () => Promise<T>): Promise<T> {
      if (this.state === 'OPEN') {
        if (Date.now() < this.nextAttempt) {
          console.log('Circuit is OPEN - failing fast');
          throw new Error('Circuit breaker is open');
        } else {
          console.log('Circuit is attempting reset (half-open)');
          this.state = 'CLOSED';
        }
      }
      
      try {
        const result = await operation();
        // Success resets the failure count
        this.failures = 0;
        return result;
      } catch (error) {
        this.failures++;
        console.log(`Operation failed, failures count: ${this.failures}/${this.threshold}`);
        
        if (this.failures >= this.threshold) {
          this.state = 'OPEN';
          this.nextAttempt = Date.now() + this.timeout;
          console.log(`Circuit OPENED until: ${new Date(this.nextAttempt).toISOString()}`);
        }
        
        throw error;
      }
    }
  }
  
  // Create a circuit breaker
  const breaker = new CircuitBreaker(2, 3000);
  
  // Function that combines retry with circuit breaker
  async function executeWithRetryAndCircuitBreaker<T>(
    operation: () => Promise<T>,
    retries: number = 3
  ): Promise<T> {
    try {
      // Circuit breaker wraps the retry mechanism
      return await breaker.execute(async () => {
        let attempt = 0;
        let lastError: any;
        
        while (attempt <= retries) {
          try {
            if (attempt > 0) {
              const delay = 1000 * attempt;
              console.log(`Retry ${attempt}/${retries}, waiting ${delay}ms...`);
              await new Promise(resolve => setTimeout(resolve, delay));
            }
            
            return await operation();
          } catch (error) {
            lastError = error;
            attempt++;
            
            if (attempt > retries) {
              console.log('All retry attempts failed');
              throw error;
            }
          }
        }
        
        throw lastError;
      });
    } catch (error) {
      console.log('Operation failed with combined retry and circuit breaker:', error.message);
      throw error;
    }
  }
  
  // Unreliable operation that will always fail for this example
  async function alwaysFailingService(): Promise<string> {
    console.log('Service called - this will fail');
    throw new Error('Service unavailable');
  }
  
  // Demonstrate the combined pattern
  for (let i = 1; i <= 3; i++) {
    console.log(`\nExecution round ${i}:`);
    try {
      const result = await executeWithRetryAndCircuitBreaker(alwaysFailingService, 2);
      console.log('Result:', result);
    } catch (error) {
      console.log(`Execution ${i} failed:`, error.message);
    }
    
    // Add a delay between rounds
    if (i < 3) {
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
  }
}

// Run all examples
async function runAllExamples() {
  await basicRetryExample();
  await customRetryExample();
  await retryWithCircuitBreakerExample();
  
  console.log('\nAll retry pattern examples completed.');
}

// Execute the examples
runAllExamples().catch(error => {
  console.error('Error running examples:', error);
});
