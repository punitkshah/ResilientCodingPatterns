# Saga Pattern

## Overview

The Saga pattern is a way to manage distributed transactions across multiple services where traditional ACID transactions aren't feasible. It's particularly important in microservices architectures, where each service has its own database and operations need to span multiple services.

## Problem Statement

In a microservices architecture, we often need to maintain data consistency across services. However, traditional distributed transactions can lead to resource locking and availability issues, which defeats many benefits of microservices.

## Solution: The Saga Pattern

A saga is a sequence of local transactions. If any transaction fails, compensating transactions are executed to undo the changes made by the preceding transactions.

There are two main ways to implement the saga pattern:

1. **Choreography**: Each service publishes events, and other services subscribe to those events and take appropriate actions.
2. **Orchestration**: A central coordinator (the saga orchestrator) directs the participant services and manages the overall saga.

## Benefits

- Maintains data consistency across multiple services without distributed transactions
- Improves system resilience by defining clear compensating actions
- Allows each service to use its own database technology (SQL, NoSQL, etc.)

## Implementation Considerations

- Saga transactions are eventually consistent, not ACID-compliant
- Compensating transactions must be idempotent
- The system must handle partially completed sagas during recovery
- The design must account for concurrency issues

## Code Example: Orchestration-based Saga in C#

```csharp
public class OrderSaga
{
    private readonly IOrderService _orderService;
    private readonly IPaymentService _paymentService;
    private readonly IInventoryService _inventoryService;
    private readonly IShippingService _shippingService;
    private readonly ILogger<OrderSaga> _logger;

    public OrderSaga(
        IOrderService orderService,
        IPaymentService paymentService,
        IInventoryService inventoryService,
        IShippingService shippingService,
        ILogger<OrderSaga> logger)
    {
        _orderService = orderService;
        _paymentService = paymentService;
        _inventoryService = inventoryService;
        _shippingService = shippingService;
        _logger = logger;
    }

    public async Task<bool> ProcessOrder(OrderDto order)
    {
        // Start saga
        _logger.LogInformation($"Starting saga for order {order.Id}");
        
        // Step 1: Create order
        var orderCreated = await _orderService.CreateOrder(order);
        if (!orderCreated)
        {
            _logger.LogError($"Failed to create order {order.Id}");
            return false;
        }

        // Step 2: Reserve inventory
        var inventoryReserved = await _inventoryService.ReserveInventory(order.Items);
        if (!inventoryReserved)
        {
            _logger.LogError($"Failed to reserve inventory for order {order.Id}");
            // Compensating transaction
            await _orderService.CancelOrder(order.Id);
            return false;
        }

        // Step 3: Process payment
        var paymentProcessed = await _paymentService.ProcessPayment(order.Id, order.Payment);
        if (!paymentProcessed)
        {
            _logger.LogError($"Failed to process payment for order {order.Id}");
            // Compensating transactions
            await _inventoryService.ReleaseInventory(order.Items);
            await _orderService.CancelOrder(order.Id);
            return false;
        }

        // Step 4: Schedule shipping
        var shippingScheduled = await _shippingService.ScheduleShipping(order.Id, order.ShippingAddress);
        if (!shippingScheduled)
        {
            _logger.LogError($"Failed to schedule shipping for order {order.Id}");
            // Compensating transactions
            await _paymentService.RefundPayment(order.Id);
            await _inventoryService.ReleaseInventory(order.Items);
            await _orderService.CancelOrder(order.Id);
            return false;
        }

        _logger.LogInformation($"Saga completed successfully for order {order.Id}");
        return true;
    }
}
```

## Code Example: Choreography-based Saga in TypeScript

```typescript
// Order Service
export class OrderService {
  private eventBus: EventBus;
  
  constructor(eventBus: EventBus) {
    this.eventBus = eventBus;
    
    // Listen for compensation events
    this.eventBus.subscribe('PaymentFailed', this.handlePaymentFailed.bind(this));
    this.eventBus.subscribe('InventoryReservationFailed', this.handleInventoryReservationFailed.bind(this));
  }
  
  async createOrder(order: Order): Promise<boolean> {
    try {
      // Save order in PENDING state
      await this.saveOrder({ ...order, status: 'PENDING' });
      
      // Publish event for next step in the saga
      this.eventBus.publish('OrderCreated', { orderId: order.id, items: order.items });
      return true;
    } catch (error) {
      console.error(`Failed to create order ${order.id}:`, error);
      return false;
    }
  }
  
  private async handlePaymentFailed(data: { orderId: string }): Promise<void> {
    await this.updateOrderStatus(data.orderId, 'CANCELLED');
    console.log(`Order ${data.orderId} cancelled due to payment failure`);
  }
  
  private async handleInventoryReservationFailed(data: { orderId: string }): Promise<void> {
    await this.updateOrderStatus(data.orderId, 'CANCELLED');
    console.log(`Order ${data.orderId} cancelled due to inventory reservation failure`);
  }
  
  // Other methods...
}

// Inventory Service
export class InventoryService {
  private eventBus: EventBus;
  
  constructor(eventBus: EventBus) {
    this.eventBus = eventBus;
    
    // Listen for events that trigger this service
    this.eventBus.subscribe('OrderCreated', this.handleOrderCreated.bind(this));
  }
  
  private async handleOrderCreated(data: { orderId: string, items: OrderItem[] }): Promise<void> {
    try {
      const reserved = await this.reserveInventory(data.items);
      if (reserved) {
        this.eventBus.publish('InventoryReserved', { orderId: data.orderId });
      } else {
        this.eventBus.publish('InventoryReservationFailed', { orderId: data.orderId });
      }
    } catch (error) {
      console.error(`Failed to reserve inventory for order ${data.orderId}:`, error);
      this.eventBus.publish('InventoryReservationFailed', { orderId: data.orderId });
    }
  }
  
  // Other methods...
}

// Similar implementation for PaymentService and ShippingService
```

## When to Use

- When you need to maintain data consistency across multiple services
- When transactions span multiple databases or services
- In microservices architectures where each service has its own database

## When Not to Use

- When ACID transactions are genuinely required
- For simple applications with a single database
- When immediate consistency is more important than availability

## Related Patterns

- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Retry Pattern](02-retry-pattern.md)
- Compensating Transaction Pattern
