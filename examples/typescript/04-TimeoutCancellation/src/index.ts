import axios from 'axios';
import pTimeout from 'p-timeout';

console.log('Timeout and Cancellation Pattern Examples');
console.log('========================================\n');

// Example 1: Basic timeout using AbortController
async function basicTimeoutExample(): Promise<void> {
  console.log('Example 1: Basic Timeout with AbortController');
  console.log('------------------------------------------');

  try {
    // Create an AbortController
    const controller = new AbortController();
    const timeoutId = setTimeout(() => {
      console.log('Timeout reached - aborting operation');
      controller.abort();
    }, 2000); // 2 second timeout

    console.log('Starting operation with 2-second timeout...');
    
    // Execute operation with timeout
    await executeLongRunningOperation(controller.signal);
    
    // Clear the timeout if operation completes before timeout
    clearTimeout(timeoutId);
    console.log('Operation completed successfully');
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      console.log('Operation timed out after 2 seconds');
    } else if (error instanceof Error) {
      console.log(`Operation failed: ${error.message}`);
    } else {
      console.log('Unknown error occurred');
    }
  }
}

// Example 2: User-initiated cancellation
async function userCancellationExample(): Promise<void> {
  console.log('\nExample 2: User-initiated Cancellation');
  console.log('-------------------------------------');

  // Create an AbortController
  const controller = new AbortController();
  
  // Simulate user cancellation after 3 seconds
  const userCancellationTimeout = setTimeout(() => {
    console.log('User requested cancellation');
    controller.abort();
  }, 3000);
  
  try {
    console.log('Starting operation (will be cancelled by user)...');
    await executeLongRunningOperation(controller.signal);
    console.log('Operation completed successfully');
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      console.log('Operation was cancelled by user');
    } else if (error instanceof Error) {
      console.log(`Operation failed: ${error.message}`);
    } else {
      console.log('Unknown error occurred');
    }
  } finally {
    clearTimeout(userCancellationTimeout);
  }
}

// Example 3: Using p-timeout library for simpler timeout handling
async function pTimeoutExample(): Promise<void> {
  console.log('\nExample 3: Using p-timeout library');
  console.log('--------------------------------');

  try {
    console.log('Starting operation with 2-second timeout using p-timeout...');
    
    // Use p-timeout to add timeout to any promise
    await pTimeout(
      slowOperation(), 
      {
        milliseconds: 2000,
        message: 'Operation timed out after 2 seconds'
      }
    );
    
    console.log('Operation completed successfully');
  } catch (error) {
    if (error instanceof Error) {
      console.log(`Error: ${error.message}`);
    } else {
      console.log('Unknown error occurred');
    }
  }
}

// Example 4: Cancellable fetch with timeout and cleanup
async function cancellableFetchExample(): Promise<void> {
  console.log('\nExample 4: Cancellable HTTP Request with Cleanup');
  console.log('---------------------------------------------');

  // Create an AbortController
  const controller = new AbortController();
  
  // Set a timeout that will cancel the request after 2 seconds
  const timeoutId = setTimeout(() => {
    console.log('Timeout reached - aborting HTTP request');
    controller.abort();
  }, 2000);
  
  try {
    console.log('Starting HTTP request with 2-second timeout...');
    
    // Using axios with AbortController
    await axios.get('https://httpstat.us/200?sleep=4000', { // This endpoint delays response by 4 seconds
      signal: controller.signal
    });
    
    console.log('HTTP request completed successfully');
  } catch (error) {
    if (axios.isCancel(error)) {
      console.log('HTTP request was cancelled');
    } else if (error instanceof Error) {
      console.log(`HTTP request failed: ${error.message}`);
    } else {
      console.log('Unknown error occurred');
    }
  } finally {
    // Clean up resources
    clearTimeout(timeoutId);
    console.log('Resources cleaned up');
  }
}

// Example 5: Composite cancellation (combining timeout with user cancellation)
async function compositeCancellationExample(): Promise<void> {
  console.log('\nExample 5: Composite Cancellation');
  console.log('--------------------------------');

  // Create an AbortController for the timeout
  const timeoutController = new AbortController();
  
  // Create an AbortController for user cancellation
  const userController = new AbortController();
  
  // Set a timeout
  const timeoutId = setTimeout(() => {
    console.log('Timeout reached after 5 seconds');
    timeoutController.abort();
  }, 5000);
  
  // Simulate user cancellation after 2 seconds
  const userCancellationTimeout = setTimeout(() => {
    console.log('User requested cancellation');
    userController.abort();
  }, 2000);
  
  // Create a composite AbortSignal that triggers when either signal aborts
  const compositeCancellation = new Promise<never>((_, reject) => {
    // Function to handle abortion
    const abortHandler = (source: string) => {
      return () => {
        reject(new DOMException(`Operation aborted by ${source}`, 'AbortError'));
        cleanup();
      };
    };
    
    // Add event listeners to both controllers
    timeoutController.signal.addEventListener('abort', abortHandler('timeout'));
    userController.signal.addEventListener('abort', abortHandler('user'));
    
    // Cleanup function to remove event listeners
    const cleanup = () => {
      timeoutController.signal.removeEventListener('abort', abortHandler('timeout'));
      userController.signal.removeEventListener('abort', abortHandler('user'));
    };
  });
  
  try {
    console.log('Starting operation with composite cancellation...');
    
    // Race the operation against the composite cancellation
    await Promise.race([
      slowOperation(),
      compositeCancellation
    ]);
    
    console.log('Operation completed successfully');
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      if (timeoutController.signal.aborted) {
        console.log('Operation was cancelled due to timeout');
      } else if (userController.signal.aborted) {
        console.log('Operation was cancelled by user');
      } else {
        console.log('Operation was cancelled (unknown source)');
      }
    } else if (error instanceof Error) {
      console.log(`Operation failed: ${error.message}`);
    } else {
      console.log('Unknown error occurred');
    }
  } finally {
    // Clean up resources
    clearTimeout(timeoutId);
    clearTimeout(userCancellationTimeout);
    console.log('Resources cleaned up');
  }
}

// Helper function - simulates a long-running operation that respects cancellation
async function executeLongRunningOperation(signal?: AbortSignal): Promise<void> {
  for (let i = 0; i < 10; i++) {
    // Check for cancellation before each step
    if (signal?.aborted) {
      throw new DOMException('Operation aborted', 'AbortError');
    }
    
    console.log(`Operation step ${i+1}/10 in progress...`);
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
}

// Helper function - simulates a slow operation without built-in cancellation
async function slowOperation(): Promise<string> {
  console.log('Slow operation started');
  await new Promise(resolve => setTimeout(resolve, 4000)); // Takes 4 seconds
  console.log('Slow operation completed');
  return 'Operation result';
}

// Run all examples
async function runAllExamples(): Promise<void> {
  await basicTimeoutExample();
  await userCancellationExample();
  await pTimeoutExample();
  await cancellableFetchExample();
  await compositeCancellationExample();
  
  console.log('\nAll timeout and cancellation examples completed');
}

// Execute all examples
runAllExamples().catch(error => {
  console.error('Error running examples:', error);
});
