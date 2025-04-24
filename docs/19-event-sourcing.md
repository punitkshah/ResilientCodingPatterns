# Event Sourcing Pattern

## Overview

The Event Sourcing pattern captures all changes to an application's state as a sequence of events. Instead of storing just the current state, the application maintains a log of all state-changing events, which serves as the system of record. The current state can be reconstructed by replaying the event log.

## Problem Statement

Traditional CRUD-based systems have several limitations:
- They only store the current state, losing historical information
- Auditing and debugging become difficult without history
- Complex domain state transitions are hard to model and understand
- Concurrency conflicts can be challenging to resolve
- Scaling write operations is limited by the database design

## Solution: Event Sourcing

Store an immutable log of all events that change application state:

1. **Capture events**: Record all state changes as immutable events in an append-only log
2. **Rebuild state**: Reconstruct the current state by replaying all events
3. **Use snapshots**: Periodically save state snapshots to improve replay performance
4. **Support projections**: Create specialized read models optimized for specific queries

## Benefits

- Preserves complete history of all state changes
- Enables reliable audit logging and temporal queries
- Simplifies complex domain modeling with event-driven design
- Improves scalability by separating write and read concerns
- Enables powerful debugging and system reconstruction capabilities
- Facilitates building eventual consistency models

## Implementation Considerations

- Plan for event schema evolution as requirements change
- Implement snapshotting for performance optimization
- Design projections for efficient read operations
- Consider eventual consistency implications
- Manage event log storage growth over time

## Code Example: Event Sourcing in C#

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ResilientEventSourcing
{
    // Base interface for all events
    public interface IEvent
    {
        Guid Id { get; }
        DateTime Timestamp { get; }
        Guid AggregateId { get; }
        int Version { get; }
    }

    // Base class for domain events
    public abstract class DomainEvent : IEvent
    {
        public Guid Id { get; private set; }
        public DateTime Timestamp { get; private set; }
        public Guid AggregateId { get; private set; }
        public int Version { get; private set; }

        protected DomainEvent(Guid aggregateId, int version)
        {
            Id = Guid.NewGuid();
            Timestamp = DateTime.UtcNow;
            AggregateId = aggregateId;
            Version = version;
        }
    }

    // Example domain events for a shopping cart
    public class CartCreatedEvent : DomainEvent
    {
        public string CustomerId { get; private set; }

        public CartCreatedEvent(Guid cartId, string customerId, int version) 
            : base(cartId, version)
        {
            CustomerId = customerId;
        }
    }

    public class ProductAddedToCartEvent : DomainEvent
    {
        public string ProductId { get; private set; }
        public string ProductName { get; private set; }
        public decimal Price { get; private set; }
        public int Quantity { get; private set; }

        public ProductAddedToCartEvent(Guid cartId, string productId, string productName, 
            decimal price, int quantity, int version) 
            : base(cartId, version)
        {
            ProductId = productId;
            ProductName = productName;
            Price = price;
            Quantity = quantity;
        }
    }

    public class ProductRemovedFromCartEvent : DomainEvent
    {
        public string ProductId { get; private set; }
        public int Quantity { get; private set; }

        public ProductRemovedFromCartEvent(Guid cartId, string productId, int quantity, int version) 
            : base(cartId, version)
        {
            ProductId = productId;
            Quantity = quantity;
        }
    }

    public class CartCheckedOutEvent : DomainEvent
    {
        public DateTime CheckoutDate { get; private set; }
        public decimal TotalAmount { get; private set; }

        public CartCheckedOutEvent(Guid cartId, decimal totalAmount, int version) 
            : base(cartId, version)
        {
            CheckoutDate = DateTime.UtcNow;
            TotalAmount = totalAmount;
        }
    }

    // Event store interface
    public interface IEventStore
    {
        Task SaveEventsAsync(Guid aggregateId, IEnumerable<IEvent> events, int expectedVersion);
        Task<List<IEvent>> GetEventsForAggregateAsync(Guid aggregateId);
        Task<List<IEvent>> GetAllEventsAsync();
    }

    // Simple in-memory event store implementation
    public class InMemoryEventStore : IEventStore
    {
        private readonly Dictionary<Guid, List<IEvent>> _events = new Dictionary<Guid, List<IEvent>>();
        private readonly List<IEvent> _allEvents = new List<IEvent>();
        private readonly ILogger<InMemoryEventStore> _logger;

        public InMemoryEventStore(ILogger<InMemoryEventStore> logger)
        {
            _logger = logger;
        }

        public Task SaveEventsAsync(Guid aggregateId, IEnumerable<IEvent> events, int expectedVersion)
        {
            // Get event list for the aggregate
            if (!_events.TryGetValue(aggregateId, out var eventList))
            {
                eventList = new List<IEvent>();
                _events[aggregateId] = eventList;
            }
            
            // Check for concurrency conflicts
            var lastVersion = eventList.Count == 0 ? -1 : eventList.Max(e => e.Version);
            
            if (lastVersion != expectedVersion && expectedVersion != -1)
            {
                throw new ConcurrencyException(
                    $"Concurrency conflict detected for aggregate {aggregateId}. " +
                    $"Expected version {expectedVersion}, but got {lastVersion}");
            }

            // Add events to the store
            foreach (var @event in events)
            {
                eventList.Add(@event);
                _allEvents.Add(@event);
                
                _logger.LogInformation(
                    "Event {EventType} saved for aggregate {AggregateId}, version {Version}", 
                    @event.GetType().Name, aggregateId, @event.Version);
            }

            return Task.CompletedTask;
        }

        public Task<List<IEvent>> GetEventsForAggregateAsync(Guid aggregateId)
        {
            if (!_events.TryGetValue(aggregateId, out var eventList))
            {
                return Task.FromResult(new List<IEvent>());
            }

            return Task.FromResult(new List<IEvent>(eventList));
        }

        public Task<List<IEvent>> GetAllEventsAsync()
        {
            return Task.FromResult(new List<IEvent>(_allEvents));
        }
    }

    // Exception for concurrency conflicts
    public class ConcurrencyException : Exception
    {
        public ConcurrencyException(string message) : base(message) { }
    }

    // Base class for aggregates
    public abstract class AggregateRoot
    {
        private readonly List<IEvent> _changes = new List<IEvent>();
        
        public Guid Id { get; protected set; }
        public int Version { get; protected set; } = -1;

        // Get uncommitted changes
        public IEnumerable<IEvent> GetUncommittedChanges()
        {
            return _changes;
        }

        // Mark changes as committed
        public void MarkChangesAsCommitted()
        {
            _changes.Clear();
        }

        // Load aggregate from history
        public void LoadFromHistory(IEnumerable<IEvent> history)
        {
            foreach (var @event in history)
            {
                ApplyChange(@event, false);
                Version = @event.Version;
            }
        }

        // Apply a new change
        protected void ApplyChange(IEvent @event)
        {
            ApplyChange(@event, true);
        }

        // Apply the event to the aggregate
        private void ApplyChange(IEvent @event, bool isNew)
        {
            ((dynamic)this).Apply((dynamic)@event);
            
            if (isNew)
            {
                _changes.Add(@event);
            }
        }
    }

    // Shopping cart aggregate
    public class ShoppingCart : AggregateRoot
    {
        public string CustomerId { get; private set; }
        public Dictionary<string, CartItem> Items { get; private set; } = new Dictionary<string, CartItem>();
        public bool IsCheckedOut { get; private set; }
        public decimal TotalAmount { get; private set; }

        public ShoppingCart() { } // Required for loading from history

        public ShoppingCart(Guid id, string customerId)
        {
            ApplyChange(new CartCreatedEvent(id, customerId, 0));
        }

        // Add product to cart
        public void AddProduct(string productId, string productName, decimal price, int quantity)
        {
            if (IsCheckedOut)
                throw new InvalidOperationException("Cannot modify a checked-out cart");

            if (quantity <= 0)
                throw new ArgumentException("Quantity must be positive", nameof(quantity));

            ApplyChange(new ProductAddedToCartEvent(Id, productId, productName, price, quantity, Version + 1));
        }

        // Remove product from cart
        public void RemoveProduct(string productId, int quantity)
        {
            if (IsCheckedOut)
                throw new InvalidOperationException("Cannot modify a checked-out cart");

            if (!Items.TryGetValue(productId, out var item))
                throw new InvalidOperationException($"Product {productId} not in cart");

            if (quantity <= 0)
                throw new ArgumentException("Quantity must be positive", nameof(quantity));

            if (quantity > item.Quantity)
                throw new InvalidOperationException("Cannot remove more items than are in the cart");

            ApplyChange(new ProductRemovedFromCartEvent(Id, productId, quantity, Version + 1));
        }

        // Checkout the cart
        public void Checkout()
        {
            if (IsCheckedOut)
                throw new InvalidOperationException("Cart is already checked out");

            if (!Items.Any())
                throw new InvalidOperationException("Cannot checkout an empty cart");

            decimal total = Items.Values.Sum(i => i.Price * i.Quantity);
            ApplyChange(new CartCheckedOutEvent(Id, total, Version + 1));
        }

        // Event handlers
        public void Apply(CartCreatedEvent @event)
        {
            Id = @event.AggregateId;
            CustomerId = @event.CustomerId;
            Items = new Dictionary<string, CartItem>();
            IsCheckedOut = false;
        }

        public void Apply(ProductAddedToCartEvent @event)
        {
            if (!Items.TryGetValue(@event.ProductId, out var existingItem))
            {
                Items[@event.ProductId] = new CartItem
                {
                    ProductId = @event.ProductId,
                    ProductName = @event.ProductName,
                    Price = @event.Price,
                    Quantity = @event.Quantity
                };
            }
            else
            {
                existingItem.Quantity += @event.Quantity;
            }
        }

        public void Apply(ProductRemovedFromCartEvent @event)
        {
            if (Items.TryGetValue(@event.ProductId, out var existingItem))
            {
                if (existingItem.Quantity <= @event.Quantity)
                {
                    Items.Remove(@event.ProductId);
                }
                else
                {
                    existingItem.Quantity -= @event.Quantity;
                }
            }
        }

        public void Apply(CartCheckedOutEvent @event)
        {
            IsCheckedOut = true;
            TotalAmount = @event.TotalAmount;
        }
    }

    // Cart item
    public class CartItem
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    // Repository for shopping carts
    public class ShoppingCartRepository
    {
        private readonly IEventStore _eventStore;
        private readonly ILogger<ShoppingCartRepository> _logger;

        public ShoppingCartRepository(IEventStore eventStore, ILogger<ShoppingCartRepository> logger)
        {
            _eventStore = eventStore;
            _logger = logger;
        }

        public async Task<ShoppingCart> GetByIdAsync(Guid id)
        {
            var events = await _eventStore.GetEventsForAggregateAsync(id);
            
            if (!events.Any())
            {
                return null;
            }

            var cart = new ShoppingCart();
            cart.LoadFromHistory(events);
            return cart;
        }

        public async Task SaveAsync(ShoppingCart cart)
        {
            await _eventStore.SaveEventsAsync(
                cart.Id,
                cart.GetUncommittedChanges(),
                cart.Version);
                
            cart.MarkChangesAsCommitted();
        }
    }

    // Read model / projection for shopping carts
    public class ShoppingCartProjection
    {
        private readonly Dictionary<Guid, ShoppingCartReadModel> _carts = new Dictionary<Guid, ShoppingCartReadModel>();
        private readonly ILogger<ShoppingCartProjection> _logger;

        public ShoppingCartProjection(ILogger<ShoppingCartProjection> logger)
        {
            _logger = logger;
        }

        public void Handle(CartCreatedEvent @event)
        {
            _carts[@event.AggregateId] = new ShoppingCartReadModel
            {
                Id = @event.AggregateId,
                CustomerId = @event.CustomerId,
                Items = new List<CartItemReadModel>(),
                Status = "New",
                CreatedAt = @event.Timestamp
            };
            
            _logger.LogInformation("Projection updated: Cart created for customer {CustomerId}", @event.CustomerId);
        }

        public void Handle(ProductAddedToCartEvent @event)
        {
            if (_carts.TryGetValue(@event.AggregateId, out var cart))
            {
                var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == @event.ProductId);
                
                if (existingItem != null)
                {
                    existingItem.Quantity += @event.Quantity;
                }
                else
                {
                    cart.Items.Add(new CartItemReadModel
                    {
                        ProductId = @event.ProductId,
                        ProductName = @event.ProductName,
                        Price = @event.Price,
                        Quantity = @event.Quantity
                    });
                }
                
                cart.LastUpdated = @event.Timestamp;
                cart.TotalItems = cart.Items.Sum(i => i.Quantity);
                cart.TotalAmount = cart.Items.Sum(i => i.Price * i.Quantity);
                
                _logger.LogInformation(
                    "Projection updated: Product {ProductId} added to cart {CartId}", 
                    @event.ProductId, @event.AggregateId);
            }
        }

        public void Handle(ProductRemovedFromCartEvent @event)
        {
            if (_carts.TryGetValue(@event.AggregateId, out var cart))
            {
                var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == @event.ProductId);
                
                if (existingItem != null)
                {
                    if (existingItem.Quantity <= @event.Quantity)
                    {
                        cart.Items.Remove(existingItem);
                    }
                    else
                    {
                        existingItem.Quantity -= @event.Quantity;
                    }
                    
                    cart.LastUpdated = @event.Timestamp;
                    cart.TotalItems = cart.Items.Sum(i => i.Quantity);
                    cart.TotalAmount = cart.Items.Sum(i => i.Price * i.Quantity);
                    
                    _logger.LogInformation(
                        "Projection updated: Product {ProductId} removed from cart {CartId}", 
                        @event.ProductId, @event.AggregateId);
                }
            }
        }

        public void Handle(CartCheckedOutEvent @event)
        {
            if (_carts.TryGetValue(@event.AggregateId, out var cart))
            {
                cart.Status = "Checked Out";
                cart.CheckoutDate = @event.CheckoutDate;
                cart.LastUpdated = @event.Timestamp;
                
                _logger.LogInformation(
                    "Projection updated: Cart {CartId} checked out with total amount {TotalAmount}", 
                    @event.AggregateId, @event.TotalAmount);
            }
        }

        public ShoppingCartReadModel GetCart(Guid cartId)
        {
            return _carts.TryGetValue(cartId, out var cart) ? cart : null;
        }

        public IEnumerable<ShoppingCartReadModel> GetCartsByCustomer(string customerId)
        {
            return _carts.Values.Where(c => c.CustomerId == customerId).ToList();
        }

        public IEnumerable<ShoppingCartReadModel> GetRecentCarts(int count)
        {
            return _carts.Values
                .OrderByDescending(c => c.LastUpdated ?? c.CreatedAt)
                .Take(count)
                .ToList();
        }
    }

    // Read model for shopping carts
    public class ShoppingCartReadModel
    {
        public Guid Id { get; set; }
        public string CustomerId { get; set; }
        public List<CartItemReadModel> Items { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUpdated { get; set; }
        public DateTime? CheckoutDate { get; set; }
        public int TotalItems { get; set; }
        public decimal TotalAmount { get; set; }
    }

    // Read model for cart items
    public class CartItemReadModel
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    // Event dispatcher to update projections
    public class EventDispatcher
    {
        private readonly ShoppingCartProjection _cartProjection;
        private readonly ILogger<EventDispatcher> _logger;

        public EventDispatcher(
            ShoppingCartProjection cartProjection,
            ILogger<EventDispatcher> logger)
        {
            _cartProjection = cartProjection;
            _logger = logger;
        }

        public void Dispatch(IEvent @event)
        {
            switch (@event)
            {
                case CartCreatedEvent cartCreated:
                    _cartProjection.Handle(cartCreated);
                    break;
                    
                case ProductAddedToCartEvent productAdded:
                    _cartProjection.Handle(productAdded);
                    break;
                    
                case ProductRemovedFromCartEvent productRemoved:
                    _cartProjection.Handle(productRemoved);
                    break;
                    
                case CartCheckedOutEvent cartCheckedOut:
                    _cartProjection.Handle(cartCheckedOut);
                    break;
                    
                default:
                    _logger.LogWarning("Unknown event type: {@EventType}", @event.GetType().Name);
                    break;
            }
        }
    }

    // Example service that uses event sourcing
    public class ShoppingCartService
    {
        private readonly ShoppingCartRepository _repository;
        private readonly EventDispatcher _eventDispatcher;
        private readonly ILogger<ShoppingCartService> _logger;

        public ShoppingCartService(
            ShoppingCartRepository repository,
            EventDispatcher eventDispatcher,
            ILogger<ShoppingCartService> logger)
        {
            _repository = repository;
            _eventDispatcher = eventDispatcher;
            _logger = logger;
        }

        public async Task<Guid> CreateCartAsync(string customerId)
        {
            var cartId = Guid.NewGuid();
            var cart = new ShoppingCart(cartId, customerId);
            
            await _repository.SaveAsync(cart);
            
            // Dispatch events to update projections
            foreach (var @event in cart.GetUncommittedChanges())
            {
                _eventDispatcher.Dispatch(@event);
            }
            
            return cartId;
        }

        public async Task AddProductToCartAsync(Guid cartId, string productId, 
            string productName, decimal price, int quantity)
        {
            var cart = await _repository.GetByIdAsync(cartId);
            
            if (cart == null)
            {
                throw new KeyNotFoundException($"Cart {cartId} not found");
            }
            
            cart.AddProduct(productId, productName, price, quantity);
            await _repository.SaveAsync(cart);
            
            // Dispatch events to update projections
            foreach (var @event in cart.GetUncommittedChanges())
            {
                _eventDispatcher.Dispatch(@event);
            }
        }

        public async Task RemoveProductFromCartAsync(Guid cartId, string productId, int quantity)
        {
            var cart = await _repository.GetByIdAsync(cartId);
            
            if (cart == null)
            {
                throw new KeyNotFoundException($"Cart {cartId} not found");
            }
            
            cart.RemoveProduct(productId, quantity);
            await _repository.SaveAsync(cart);
            
            // Dispatch events to update projections
            foreach (var @event in cart.GetUncommittedChanges())
            {
                _eventDispatcher.Dispatch(@event);
            }
        }

        public async Task CheckoutCartAsync(Guid cartId)
        {
            var cart = await _repository.GetByIdAsync(cartId);
            
            if (cart == null)
            {
                throw new KeyNotFoundException($"Cart {cartId} not found");
            }
            
            cart.Checkout();
            await _repository.SaveAsync(cart);
            
            // Dispatch events to update projections
            foreach (var @event in cart.GetUncommittedChanges())
            {
                _eventDispatcher.Dispatch(@event);
            }
        }
    }
}
```

## Code Example: Event Sourcing in TypeScript

```typescript
// Event Sourcing implementation in TypeScript

// Base event interface
interface Event {
  id: string;
  timestamp: Date;
  aggregateId: string;
  version: number;
}

// Base domain event
abstract class DomainEvent implements Event {
  public readonly id: string;
  public readonly timestamp: Date;
  public readonly aggregateId: string;
  public readonly version: number;

  constructor(aggregateId: string, version: number) {
    this.id = crypto.randomUUID();
    this.timestamp = new Date();
    this.aggregateId = aggregateId;
    this.version = version;
  }
}

// Shopping cart events
class CartCreatedEvent extends DomainEvent {
  public readonly customerId: string;

  constructor(cartId: string, customerId: string, version: number) {
    super(cartId, version);
    this.customerId = customerId;
  }
}

class ProductAddedToCartEvent extends DomainEvent {
  public readonly productId: string;
  public readonly productName: string;
  public readonly price: number;
  public readonly quantity: number;

  constructor(
    cartId: string,
    productId: string,
    productName: string,
    price: number,
    quantity: number,
    version: number
  ) {
    super(cartId, version);
    this.productId = productId;
    this.productName = productName;
    this.price = price;
    this.quantity = quantity;
  }
}

class ProductRemovedFromCartEvent extends DomainEvent {
  public readonly productId: string;
  public readonly quantity: number;

  constructor(cartId: string, productId: string, quantity: number, version: number) {
    super(cartId, version);
    this.productId = productId;
    this.quantity = quantity;
  }
}

class CartCheckedOutEvent extends DomainEvent {
  public readonly checkoutDate: Date;
  public readonly totalAmount: number;

  constructor(cartId: string, totalAmount: number, version: number) {
    super(cartId, version);
    this.checkoutDate = new Date();
    this.totalAmount = totalAmount;
  }
}

// Event store interface
interface EventStore {
  saveEvents(aggregateId: string, events: Event[], expectedVersion: number): Promise<void>;
  getEventsForAggregate(aggregateId: string): Promise<Event[]>;
  getAllEvents(): Promise<Event[]>;
}

// In-memory event store implementation
class InMemoryEventStore implements EventStore {
  private events: Map<string, Event[]> = new Map();
  private allEvents: Event[] = [];

  constructor() {
    console.log('Initializing in-memory event store');
  }

  async saveEvents(aggregateId: string, events: Event[], expectedVersion: number): Promise<void> {
    // Get the event list for this aggregate
    let eventList = this.events.get(aggregateId) || [];
    
    // Check for concurrency conflicts
    const lastVersion = eventList.length === 0 ? -1 : 
      Math.max(...eventList.map(e => e.version));
      
    if (lastVersion !== expectedVersion && expectedVersion !== -1) {
      throw new Error(
        `Concurrency conflict detected for aggregate ${aggregateId}. ` +
        `Expected version ${expectedVersion}, but got ${lastVersion}`
      );
    }

    // Add events to the store
    for (const event of events) {
      eventList.push(event);
      this.allEvents.push(event);
      console.log(
        `Event ${event.constructor.name} saved for aggregate ${aggregateId}, version ${event.version}`
      );
    }

    this.events.set(aggregateId, eventList);
  }

  async getEventsForAggregate(aggregateId: string): Promise<Event[]> {
    return this.events.get(aggregateId) || [];
  }

  async getAllEvents(): Promise<Event[]> {
    return [...this.allEvents];
  }
}

// Base aggregate root
abstract class AggregateRoot {
  private changes: Event[] = [];
  
  public id: string;
  public version: number = -1;

  // Get uncommitted changes
  public getUncommittedChanges(): Event[] {
    return [...this.changes];
  }

  // Mark changes as committed
  public markChangesAsCommitted(): void {
    this.changes = [];
  }

  // Load aggregate from history
  public loadFromHistory(history: Event[]): void {
    for (const event of history) {
      this.applyChange(event, false);
      this.version = event.version;
    }
  }

  // Apply a new change
  protected applyChange(event: Event, isNew: boolean = true): void {
    // Use dynamic dispatch to call the appropriate handler
    const handler = `apply${event.constructor.name}`;
    if (typeof this[handler] === 'function') {
      this[handler](event);
    } else {
      console.warn(`No handler found for event ${event.constructor.name}`);
    }
    
    if (isNew) {
      this.changes.push(event);
    }
  }
}

// Cart item type
interface CartItem {
  productId: string;
  productName: string;
  price: number;
  quantity: number;
}

// Shopping cart aggregate
class ShoppingCart extends AggregateRoot {
  private customerId: string;
  private items: Map<string, CartItem> = new Map();
  private isCheckedOut: boolean = false;
  private totalAmount: number = 0;

  constructor(id?: string, customerId?: string) {
    super();
    
    if (id && customerId) {
      this.applyChange(new CartCreatedEvent(id, customerId, 0));
    }
  }

  // Add product to cart
  public addProduct(productId: string, productName: string, price: number, quantity: number): void {
    if (this.isCheckedOut) {
      throw new Error('Cannot modify a checked-out cart');
    }

    if (quantity <= 0) {
      throw new Error('Quantity must be positive');
    }

    this.applyChange(
      new ProductAddedToCartEvent(this.id, productId, productName, price, quantity, this.version + 1)
    );
  }

  // Remove product from cart
  public removeProduct(productId: string, quantity: number): void {
    if (this.isCheckedOut) {
      throw new Error('Cannot modify a checked-out cart');
    }

    const item = this.items.get(productId);
    if (!item) {
      throw new Error(`Product ${productId} not in cart`);
    }

    if (quantity <= 0) {
      throw new Error('Quantity must be positive');
    }

    if (quantity > item.quantity) {
      throw new Error('Cannot remove more items than are in the cart');
    }

    this.applyChange(
      new ProductRemovedFromCartEvent(this.id, productId, quantity, this.version + 1)
    );
  }

  // Checkout the cart
  public checkout(): void {
    if (this.isCheckedOut) {
      throw new Error('Cart is already checked out');
    }

    if (this.items.size === 0) {
      throw new Error('Cannot checkout an empty cart');
    }

    let total = 0;
    for (const item of this.items.values()) {
      total += item.price * item.quantity;
    }

    this.applyChange(
      new CartCheckedOutEvent(this.id, total, this.version + 1)
    );
  }

  // Event handlers
  private applyCartCreatedEvent(event: CartCreatedEvent): void {
    this.id = event.aggregateId;
    this.customerId = event.customerId;
    this.items = new Map();
    this.isCheckedOut = false;
  }

  private applyProductAddedToCartEvent(event: ProductAddedToCartEvent): void {
    const existingItem = this.items.get(event.productId);
    
    if (existingItem) {
      existingItem.quantity += event.quantity;
    } else {
      this.items.set(event.productId, {
        productId: event.productId,
        productName: event.productName,
        price: event.price,
        quantity: event.quantity
      });
    }
  }

  private applyProductRemovedFromCartEvent(event: ProductRemovedFromCartEvent): void {
    const existingItem = this.items.get(event.productId);
    
    if (existingItem) {
      if (existingItem.quantity <= event.quantity) {
        this.items.delete(event.productId);
      } else {
        existingItem.quantity -= event.quantity;
      }
    }
  }

  private applyCartCheckedOutEvent(event: CartCheckedOutEvent): void {
    this.isCheckedOut = true;
    this.totalAmount = event.totalAmount;
  }

  // Getters for the internal state
  public getCustomerId(): string {
    return this.customerId;
  }

  public getItems(): CartItem[] {
    return Array.from(this.items.values());
  }

  public getIsCheckedOut(): boolean {
    return this.isCheckedOut;
  }

  public getTotalAmount(): number {
    return this.totalAmount;
  }
}

// Shopping cart repository
class ShoppingCartRepository {
  private eventStore: EventStore;

  constructor(eventStore: EventStore) {
    this.eventStore = eventStore;
  }

  async getById(id: string): Promise<ShoppingCart | null> {
    const events = await this.eventStore.getEventsForAggregate(id);
    
    if (events.length === 0) {
      return null;
    }

    const cart = new ShoppingCart();
    cart.loadFromHistory(events);
    return cart;
  }

  async save(cart: ShoppingCart): Promise<void> {
    await this.eventStore.saveEvents(
      cart.id,
      cart.getUncommittedChanges(),
      cart.version
    );
    
    cart.markChangesAsCommitted();
  }
}

// Read model interfaces
interface CartItemReadModel {
  productId: string;
  productName: string;
  price: number;
  quantity: number;
}

interface ShoppingCartReadModel {
  id: string;
  customerId: string;
  items: CartItemReadModel[];
  status: string;
  createdAt: Date;
  lastUpdated?: Date;
  checkoutDate?: Date;
  totalItems: number;
  totalAmount: number;
}

// Shopping cart projection
class ShoppingCartProjection {
  private carts: Map<string, ShoppingCartReadModel> = new Map();

  handleCartCreatedEvent(event: CartCreatedEvent): void {
    this.carts.set(event.aggregateId, {
      id: event.aggregateId,
      customerId: event.customerId,
      items: [],
      status: 'New',
      createdAt: event.timestamp,
      totalItems: 0,
      totalAmount: 0
    });
    
    console.log(`Projection updated: Cart created for customer ${event.customerId}`);
  }

  handleProductAddedToCartEvent(event: ProductAddedToCartEvent): void {
    const cart = this.carts.get(event.aggregateId);
    
    if (cart) {
      const existingItemIndex = cart.items.findIndex(i => i.productId === event.productId);
      
      if (existingItemIndex >= 0) {
        cart.items[existingItemIndex].quantity += event.quantity;
      } else {
        cart.items.push({
          productId: event.productId,
          productName: event.productName,
          price: event.price,
          quantity: event.quantity
        });
      }
      
      cart.lastUpdated = event.timestamp;
      cart.totalItems = cart.items.reduce((sum, item) => sum + item.quantity, 0);
      cart.totalAmount = cart.items.reduce((sum, item) => sum + (item.price * item.quantity), 0);
      
      console.log(`Projection updated: Product ${event.productId} added to cart ${event.aggregateId}`);
    }
  }

  handleProductRemovedFromCartEvent(event: ProductRemovedFromCartEvent): void {
    const cart = this.carts.get(event.aggregateId);
    
    if (cart) {
      const existingItemIndex = cart.items.findIndex(i => i.productId === event.productId);
      
      if (existingItemIndex >= 0) {
        const item = cart.items[existingItemIndex];
        
        if (item.quantity <= event.quantity) {
          cart.items.splice(existingItemIndex, 1);
        } else {
          item.quantity -= event.quantity;
        }
        
        cart.lastUpdated = event.timestamp;
        cart.totalItems = cart.items.reduce((sum, item) => sum + item.quantity, 0);
        cart.totalAmount = cart.items.reduce((sum, item) => sum + (item.price * item.quantity), 0);
        
        console.log(`Projection updated: Product ${event.productId} removed from cart ${event.aggregateId}`);
      }
    }
  }

  handleCartCheckedOutEvent(event: CartCheckedOutEvent): void {
    const cart = this.carts.get(event.aggregateId);
    
    if (cart) {
      cart.status = 'Checked Out';
      cart.checkoutDate = event.checkoutDate;
      cart.lastUpdated = event.timestamp;
      
      console.log(`Projection updated: Cart ${event.aggregateId} checked out with total amount ${event.totalAmount}`);
    }
  }

  getCart(cartId: string): ShoppingCartReadModel | undefined {
    return this.carts.get(cartId);
  }

  getCartsByCustomer(customerId: string): ShoppingCartReadModel[] {
    return Array.from(this.carts.values())
      .filter(c => c.customerId === customerId);
  }

  getRecentCarts(count: number): ShoppingCartReadModel[] {
    return Array.from(this.carts.values())
      .sort((a, b) => {
        const dateA = a.lastUpdated || a.createdAt;
        const dateB = b.lastUpdated || b.createdAt;
        return dateB.getTime() - dateA.getTime();
      })
      .slice(0, count);
  }
}

// Event dispatcher
class EventDispatcher {
  private cartProjection: ShoppingCartProjection;

  constructor(cartProjection: ShoppingCartProjection) {
    this.cartProjection = cartProjection;
  }

  dispatch(event: Event): void {
    if (event instanceof CartCreatedEvent) {
      this.cartProjection.handleCartCreatedEvent(event);
    } else if (event instanceof ProductAddedToCartEvent) {
      this.cartProjection.handleProductAddedToCartEvent(event);
    } else if (event instanceof ProductRemovedFromCartEvent) {
      this.cartProjection.handleProductRemovedFromCartEvent(event);
    } else if (event instanceof CartCheckedOutEvent) {
      this.cartProjection.handleCartCheckedOutEvent(event);
    } else {
      console.warn(`Unknown event type: ${event.constructor.name}`);
    }
  }
}

// Shopping cart service
class ShoppingCartService {
  private repository: ShoppingCartRepository;
  private eventDispatcher: EventDispatcher;

  constructor(repository: ShoppingCartRepository, eventDispatcher: EventDispatcher) {
    this.repository = repository;
    this.eventDispatcher = eventDispatcher;
  }

  async createCart(customerId: string): Promise<string> {
    const cartId = crypto.randomUUID();
    const cart = new ShoppingCart(cartId, customerId);
    
    await this.repository.save(cart);
    
    // Dispatch events to update projections
    for (const event of cart.getUncommittedChanges()) {
      this.eventDispatcher.dispatch(event);
    }
    
    return cartId;
  }

  async addProductToCart(
    cartId: string, 
    productId: string, 
    productName: string, 
    price: number, 
    quantity: number
  ): Promise<void> {
    const cart = await this.repository.getById(cartId);
    
    if (!cart) {
      throw new Error(`Cart ${cartId} not found`);
    }
    
    cart.addProduct(productId, productName, price, quantity);
    await this.repository.save(cart);
    
    // Dispatch events to update projections
    for (const event of cart.getUncommittedChanges()) {
      this.eventDispatcher.dispatch(event);
    }
  }

  async removeProductFromCart(cartId: string, productId: string, quantity: number): Promise<void> {
    const cart = await this.repository.getById(cartId);
    
    if (!cart) {
      throw new Error(`Cart ${cartId} not found`);
    }
    
    cart.removeProduct(productId, quantity);
    await this.repository.save(cart);
    
    // Dispatch events to update projections
    for (const event of cart.getUncommittedChanges()) {
      this.eventDispatcher.dispatch(event);
    }
  }

  async checkoutCart(cartId: string): Promise<void> {
    const cart = await this.repository.getById(cartId);
    
    if (!cart) {
      throw new Error(`Cart ${cartId} not found`);
    }
    
    cart.checkout();
    await this.repository.save(cart);
    
    // Dispatch events to update projections
    for (const event of cart.getUncommittedChanges()) {
      this.eventDispatcher.dispatch(event);
    }
  }
}

// Example usage
async function main() {
  // Set up infrastructure
  const eventStore = new InMemoryEventStore();
  const cartProjection = new ShoppingCartProjection();
  const eventDispatcher = new EventDispatcher(cartProjection);
  const cartRepository = new ShoppingCartRepository(eventStore);
  const cartService = new ShoppingCartService(cartRepository, eventDispatcher);

  try {
    // Create a new cart
    console.log('Creating a new cart...');
    const cartId = await cartService.createCart('customer123');
    console.log(`Created cart with ID: ${cartId}`);

    // Add products to the cart
    console.log('\nAdding products to the cart...');
    await cartService.addProductToCart(cartId, 'product1', 'Laptop', 999.99, 1);
    await cartService.addProductToCart(cartId, 'product2', 'Mouse', 49.99, 2);
    
    // Get the cart from the read model
    let cart = cartProjection.getCart(cartId);
    console.log('\nCart state after adding products:');
    console.log(`ID: ${cart.id}`);
    console.log(`Customer: ${cart.customerId}`);
    console.log(`Status: ${cart.status}`);
    console.log(`Total items: ${cart.totalItems}`);
    console.log(`Total amount: $${cart.totalAmount.toFixed(2)}`);
    console.log('Items:');
    cart.items.forEach(item => {
      console.log(`- ${item.productName}: $${item.price.toFixed(2)} x ${item.quantity}`);
    });

    // Remove a product
    console.log('\nRemoving a product from the cart...');
    await cartService.removeProductFromCart(cartId, 'product2', 1);
    
    // Get the updated cart
    cart = cartProjection.getCart(cartId);
    console.log('\nCart state after removing a product:');
    console.log(`Total items: ${cart.totalItems}`);
    console.log(`Total amount: $${cart.totalAmount.toFixed(2)}`);
    console.log('Items:');
    cart.items.forEach(item => {
      console.log(`- ${item.productName}: $${item.price.toFixed(2)} x ${item.quantity}`);
    });

    // Checkout the cart
    console.log('\nChecking out the cart...');
    await cartService.checkoutCart(cartId);
    
    // Get the final cart state
    cart = cartProjection.getCart(cartId);
    console.log('\nFinal cart state:');
    console.log(`Status: ${cart.status}`);
    console.log(`Checkout date: ${cart.checkoutDate}`);
    console.log(`Total amount: $${cart.totalAmount.toFixed(2)}`);

    // Get all events for the cart to demonstrate event sourcing
    const events = await eventStore.getEventsForAggregate(cartId);
    console.log('\nAll events for the cart:');
    events.forEach(event => {
      console.log(`- ${event.constructor.name} (Version: ${event.version}, Time: ${event.timestamp})`);
    });

  } catch (error) {
    console.error('Error:', error.message);
  }
}

// Run the example
main();
```

## When to Use

- For domains where the history of changes is important
- When building audit trails is a core requirement
- For systems needing to reconstruct past states
- In complex business domains where events model the reality better than state
- When you want to separate the write model from read models

## When Not to Use

- For simple CRUD applications with no need for history
- When immediate consistency is required across all views
- For systems with very high throughput requirements where the overhead is prohibitive
- If the complexity of event sourcing outweighs the benefits for your domain

## Related Patterns

- [Saga Pattern](06-saga-pattern.md)
- [CQRS Pattern](14-cqrs-pattern.md)
- [Compensating Transaction Pattern](15-compensating-transaction-pattern.md)
