import axios from 'axios';
import CircuitBreaker from 'opossum';

console.log('Circuit Breaker Pattern Examples');
console.log('================================\n');

// Configure the circuit breaker options
const circuitBreakerOptions = {
  failureThreshold: 50,      // When 50% of requests fail, open the circuit
  resetTimeout: 10000,       // After 10 seconds, try again (half-open state)
  timeout: 3000,             // Consider a request as failed if it takes more than 3 seconds
  errorThresholdPercentage: 50,  // Error percentage threshold to trip the circuit
};

// Function that we want to protect with a circuit breaker
async function makeHttpRequest(url: string): Promise<any> {
  console.log(`Sending request to ${url}...`);
  const response = await axios.get(url);
  return response.data;
}

// Create a circuit breaker for our function
const breaker = new CircuitBreaker(makeHttpRequest, circuitBreakerOptions);

// Set up event listeners to log circuit breaker state changes
breaker.on('open', () => {
  console.log('Circuit Breaker status: OPEN (failing fast)');
});

breaker.on('halfOpen', () => {
  console.log('Circuit Breaker status: HALF-OPEN (testing the service)');
});

breaker.on('close', () => {
  console.log('Circuit Breaker status: CLOSED (working normally)');
});

breaker.on('fallback', (result) => {
  console.log('Fallback called. Using cached or default response.');
});

breaker.on('timeout', (delay) => {
  console.log(`Request timed out after ${delay}ms`);
});

breaker.on('success', (result) => {
  console.log('Request successful!');
});

breaker.on('failure', (error) => {
  console.log(`Request failed: ${error.message}`);
});

breaker.fallback(() => 'Default response when circuit is open');

// Simulate some requests to demonstrate circuit breaker behavior
async function runDemo() {
  console.log('Basic Circuit Breaker Demo');
  console.log('-------------------------');

  // First demonstration - the service is unavailable, circuit will open after failures
  console.log('\nScenario 1: Service unavailable (circuit will open)');
  for (let i = 1; i <= 5; i++) {
    try {
      // Using a non-existent endpoint to simulate failures
      const result = await breaker.fire('https://this-endpoint-does-not-exist.example.com');
      console.log(`Request ${i} result:`, result);
    } catch (error) {
      console.log(`Request ${i} error: ${error.message}`);
    }
    
    // Wait 1 second between requests
    await new Promise(resolve => setTimeout(resolve, 1000));
  }

  // Wait for circuit to transition to half-open state
  console.log('\nWaiting for circuit to transition to half-open state...');
  await new Promise(resolve => setTimeout(resolve, circuitBreakerOptions.resetTimeout));

  // Second demonstration - the service becomes available again, circuit will close
  console.log('\nScenario 2: Service becomes available (circuit will close)');
  
  // Mock a working service by mocking the axios.get method
  const originalGet = axios.get;
  axios.get = async (url: string) => {
    return { data: { message: 'Service is working again!' } };
  };

  for (let i = 1; i <= 3; i++) {
    try {
      const result = await breaker.fire('https://example.com');
      console.log(`Request ${i} result:`, result);
    } catch (error) {
      console.log(`Request ${i} error: ${error.message}`);
    }
    
    // Wait 1 second between requests
    await new Promise(resolve => setTimeout(resolve, 1000));
  }

  // Restore the original axios.get method
  axios.get = originalGet;
}

// Run the demo
runDemo().catch(error => {
  console.error('Demo failed:', error);
}).finally(() => {
  console.log('\nCircuit Breaker demo completed.');
});
