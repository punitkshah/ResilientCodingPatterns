using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TimeoutCancellation
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Timeout and Cancellation Pattern Examples");
            Console.WriteLine("========================================\n");

            // Example 1: Basic timeout with CancellationTokenSource
            await BasicTimeoutExample();

            // Example 2: User-initiated cancellation
            await UserCancellationExample();

            // Example 3: Composite cancellation
            await CompositeCancellationExample();

            // Example 4: Cooperative cancellation with cleanup
            await CooperativeCancellationExample();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static async Task BasicTimeoutExample()
        {
            Console.WriteLine("Example 1: Basic Timeout with CancellationTokenSource");
            Console.WriteLine("--------------------------------------------------");

            try
            {
                // Create cancellation token with timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                Console.WriteLine("Starting operation with 2-second timeout...");
                
                // Execute operation with timeout
                await ExecuteLongRunningOperationAsync(timeoutCts.Token);
                
                Console.WriteLine("Operation completed successfully");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation timed out after 2 seconds");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Operation failed: {ex.Message}");
            }
        }

        static async Task UserCancellationExample()
        {
            Console.WriteLine("\nExample 2: User-initiated Cancellation");
            Console.WriteLine("-------------------------------------");

            using var userCancellationCts = new CancellationTokenSource();
            
            // Simulate user cancellation after 3 seconds
            var simulatedUserCancellation = Task.Delay(3000).ContinueWith(_ => 
            {
                Console.WriteLine("User requested cancellation");
                userCancellationCts.Cancel();
            });
            
            try
            {
                Console.WriteLine("Starting operation (will be cancelled by user)...");
                await ExecuteLongRunningOperationAsync(userCancellationCts.Token);
                Console.WriteLine("Operation completed successfully");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation was cancelled by user");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Operation failed: {ex.Message}");
            }
            
            // Ensure simulated user cancellation completes
            await simulatedUserCancellation;
        }

        static async Task CompositeCancellationExample()
        {
            Console.WriteLine("\nExample 3: Composite Cancellation");
            Console.WriteLine("--------------------------------");

            // Create a token source for timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            
            // Create a token source for user cancellation
            using var userCancellationCts = new CancellationTokenSource();
            
            // Create linked token source that combines both sources
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, userCancellationCts.Token);
            
            // Simulate user cancellation after 2 seconds
            var simulatedUserCancellation = Task.Delay(2000).ContinueWith(_ => 
            {
                Console.WriteLine("User requested cancellation");
                userCancellationCts.Cancel();
            });
            
            try
            {
                Console.WriteLine("Starting operation with both timeout and user cancellation...");
                await ExecuteLongRunningOperationAsync(linkedCts.Token);
                Console.WriteLine("Operation completed successfully");
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.IsCancellationRequested)
                {
                    Console.WriteLine("Operation timed out");
                }
                else if (userCancellationCts.IsCancellationRequested)
                {
                    Console.WriteLine("Operation was cancelled by user");
                }
                else
                {
                    Console.WriteLine("Operation was cancelled (unknown source)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Operation failed: {ex.Message}");
            }
            
            // Ensure simulated user cancellation completes
            await simulatedUserCancellation;
        }

        static async Task CooperativeCancellationExample()
        {
            Console.WriteLine("\nExample 4: Cooperative Cancellation with Cleanup");
            Console.WriteLine("-----------------------------------------------");

            using var cts = new CancellationTokenSource();
            
            // Start the operation in a separate task so we can cancel it
            var operationTask = Task.Run(() => CooperativeCancellableOperation(cts.Token), CancellationToken.None);
            
            // Allow the operation to run for 3 seconds then cancel
            await Task.Delay(3000);
            Console.WriteLine("Requesting cancellation of the operation...");
            cts.Cancel();
            
            try
            {
                // Wait for the operation to acknowledge the cancellation and complete
                await operationTask;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation was cancelled as expected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
        }

        static async Task ExecuteLongRunningOperationAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < 10; i++)
            {
                // Check for cancellation before each step
                cancellationToken.ThrowIfCancellationRequested();
                
                Console.WriteLine($"Operation step {i+1}/10 in progress...");
                await Task.Delay(1000, cancellationToken);
            }
        }

        static async Task CooperativeCancellableOperation(CancellationToken cancellationToken)
        {
            // Simulate acquiring resources
            Console.WriteLine("Operation started - acquiring resources...");
            
            // Register a callback that will be executed when cancellation is requested
            using var registration = cancellationToken.Register(() => 
            {
                Console.WriteLine("Cancellation notification received - will clean up resources");
            });
            
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    // Check for cancellation before each step
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("Cancellation detected - cleaning up resources...");
                        await CleanupResourcesAsync();
                        
                        // Re-throw the cancellation to signal the operation was cancelled
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    
                    Console.WriteLine($"Processing step {i+1}/10...");
                    await Task.Delay(1000, CancellationToken.None); // Use CancellationToken.None to control cleanup
                }
                
                Console.WriteLine("Operation completed successfully");
            }
            finally
            {
                // Ensure resources are cleaned up even on other exceptions
                Console.WriteLine("Ensuring all resources are cleaned up in finally block");
                await CleanupResourcesAsync();
            }
        }

        static async Task CleanupResourcesAsync()
        {
            Console.WriteLine("Starting resource cleanup...");
            await Task.Delay(500); // Simulate cleanup work
            Console.WriteLine("Resources have been properly cleaned up");
        }
    }
}
