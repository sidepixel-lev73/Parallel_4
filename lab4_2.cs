using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OrderProcessingSystem
{
    // Перечисление для статусов заказа
    public enum OrderStatus
    {
        Pending,
        Processing,
        Completed,
        Cancelled,
        PartiallyFulfilled
    }

    // Перечисление для цветов консоли
    public enum LogColor
    {
        Default = ConsoleColor.White,
        Success = ConsoleColor.Green,
        Warning = ConsoleColor.Yellow,
        Error = ConsoleColor.Red,
        Info = ConsoleColor.Cyan
    }

    // Класс товара
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int CriticalLevel { get; set; } = 3; // Критический уровень запаса

        public Product(int id, string name, decimal price, int quantity)
        {
            Id = id;
            Name = name;
            Price = price;
            Quantity = quantity;
        }
    }

    // Класс заказа
    public class Order
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public Dictionary<int, int> Items { get; set; } // ProductId -> Quantity
        public OrderStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public decimal TotalAmount { get; set; }

        public Order(int orderId, int customerId)
        {
            OrderId = orderId;
            CustomerId = customerId;
            Items = new Dictionary<int, int>();
            Status = OrderStatus.Pending;
            CreatedAt = DateTime.Now;
        }
    }

    // Основной класс системы обработки заказов
    public class OrderProcessingSystem
    {
        private readonly ConcurrentDictionary<int, Product> _catalog;
        private readonly PriorityQueue<Order, DateTime> _orderQueue;
        private readonly ConcurrentDictionary<int, Order> _pendingOrders;
        private readonly SemaphoreSlim _orderSemaphore;
        private readonly Semaphore _processingSemaphore;
        private readonly Mutex _catalogMutex;
        private readonly object _queueLock = new object();
        
        private int _orderIdCounter = 1;
        private bool _isRunning = false;
        private readonly List<Task> _customerTasks;
        private readonly List<Task> _processorTasks;

        public event Action<string, LogColor> OnLogMessage;
        public event Action<int, string> OnLowStockWarning;

        public OrderProcessingSystem()
        {
            _catalog = new ConcurrentDictionary<int, Product>();
            _orderQueue = new PriorityQueue<Order, DateTime>();
            _pendingOrders = new ConcurrentDictionary<int, Order>();
            _orderSemaphore = new SemaphoreSlim(0);
            _processingSemaphore = new Semaphore(2, 3); // 2-3 обработчика
            _catalogMutex = new Mutex();
            _customerTasks = new List<Task>();
            _processorTasks = new List<Task>();
        }

        // Инициализация каталога товаров
        public void InitializeCatalog()
        {
            var random = new Random();
            for (int i = 1; i <= 10; i++)
            {
                var product = new Product(
                    i,
                    $"Товар {i}",
                    random.Next(100, 1000),
                    random.Next(5, 21) // Количество 5-20
                );
                _catalog[i] = product;
            }

            Log($"Каталог инициализирован с {_catalog.Count} товарами", LogColor.Info);
        }

        // Запуск системы
        public void Start()
        {
            _isRunning = true;
            
            // Запуск потоков-клиентов
            for (int i = 1; i <= 12; i++) // 10-15 клиентов
            {
                int customerId = i;
                var task = Task.Run(() => CustomerProcess(customerId));
                _customerTasks.Add(task);
            }

            // Запуск потоков-обработчиков
            for (int i = 1; i <= 3; i++) // 2-3 обработчика
            {
                int processorId = i;
                var task = Task.Run(() => OrderProcessor(processorId));
                _processorTasks.Add(task);
            }

            // Запуск монитора таймаутов
            var timeoutMonitor = Task.Run(TimeoutMonitor);

            Log("Система обработки заказов запущена", LogColor.Success);
        }

        // Остановка системы
        public async Task StopAsync()
        {
            _isRunning = false;
            
            await Task.WhenAll(_customerTasks);
            await Task.WhenAll(_processorTasks);
            
            Log("Система обработки заказов остановлена", LogColor.Info);
        }

        // Процесс клиента
        private async Task CustomerProcess(int customerId)
        {
            var random = new Random();
            
            while (_isRunning)
            {
                try
                {
                    // Создание заказа
                    var order = CreateRandomOrder(customerId, random);
                    
                    AddOrderToQueue(order);
                    
                    // Ожидание перед следующим заказом
                    await Task.Delay(random.Next(1000, 3000)); // 1-3 секунды
                }
                catch (Exception ex)
                {
                    Log($"Ошибка в процессе клиента {customerId}: {ex.Message}", LogColor.Error);
                }
            }
        }

        // Создание случайного заказа
        private Order CreateRandomOrder(int customerId, Random random)
        {
            var order = new Order(Interlocked.Increment(ref _orderIdCounter), customerId);
            
            // 1-3 случайных товара из каталога
            int itemCount = random.Next(1, 4);
            
            for (int i = 0; i < itemCount; i++)
            {
                int productId = random.Next(1, _catalog.Count + 1);
                int quantity = random.Next(1, 5);
                
                if (order.Items.ContainsKey(productId))
                {
                    order.Items[productId] += quantity;
                }
                else
                {
                    order.Items[productId] = quantity;
                }
            }
            
            Log($"Клиент {customerId} создал заказ {order.OrderId}", LogColor.Info);
            return order;
        }

        // Добавление заказа в очередь
        private void AddOrderToQueue(Order order)
        {
            lock (_queueLock)
            {
                _orderQueue.Enqueue(order, order.CreatedAt);
                _pendingOrders[order.OrderId] = order;
                _orderSemaphore.Release();
                
                Log($"Заказ {order.OrderId} добавлен в очередь", LogColor.Default);
            }
        }

        // Процессор заказов
        private async Task OrderProcessor(int processorId)
        {
            while (_isRunning)
            {
                try
                {
                    await _orderSemaphore.WaitAsync();
                    
                    _processingSemaphore.WaitOne();
                    
                    Order order = null;
                    lock (_queueLock)
                    {
                        if (_orderQueue.Count > 0)
                        {
                            _orderQueue.TryDequeue(out order, out _);
                        }
                    }

                    if (order != null && _pendingOrders.TryRemove(order.OrderId, out _))
                    {
                        await ProcessOrder(order, processorId);
                    }
                    
                    _processingSemaphore.Release();
                }
                catch (Exception ex)
                {
                    Log($"Ошибка в обработчике {processorId}: {ex.Message}", LogColor.Error);
                    _processingSemaphore.Release();
                }
            }
        }

        // Обработка заказа
        private async Task ProcessOrder(Order order, int processorId)
        {
            order.Status = OrderStatus.Processing;
            
            Log($"Обработчик {processorId} начал обработку заказа {order.OrderId}", LogColor.Info);

            // Логика обработки заказа:
            // 1. Проверить доступность товаров
            // 2. Выполнить частичное или полное выполнение
            // 3. Обновить остатки товаров
            // 4. Проверить критические уровни

            try
            {
                // Имитация обработки
                await Task.Delay(2000);
                
                bool success = await TryFulfillOrder(order);
                
                if (success)
                {
                    order.Status = OrderStatus.Completed;
                    Log($"Заказ {order.OrderId} успешно обработан", LogColor.Success);
                }
                else
                {
                    order.Status = OrderStatus.Cancelled;
                    Log($"Заказ {order.OrderId} отменен (недостаточно товаров)", LogColor.Warning);
                }
                
                order.ProcessedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                Log($"Ошибка при обработке заказа {order.OrderId}: {ex.Message}", LogColor.Error);
                order.Status = OrderStatus.Cancelled;
            }
        }

        // Попытка выполнить заказ
        private async Task<bool> TryFulfillOrder(Order order)
        {
            // Использовать _catalogMutex для защиты от гонки данных
            
            bool allItemsAvailable = true;
            
            _catalogMutex.WaitOne();
            try
            {
                // Проверка доступности всех товаров
                foreach (var item in order.Items)
                {
                    if (_catalog.TryGetValue(item.Key, out var product))
                    {
                        if (product.Quantity < item.Value)
                        {
                            allItemsAvailable = false;
                            break;
                        }
                    }
                    else
                    {
                        allItemsAvailable = false;
                        break;
                    }
                }

                if (allItemsAvailable)
                {
                    // Списание товаров
                    foreach (var item in order.Items)
                    {
                        if (_catalog.TryGetValue(item.Key, out var product))
                        {
                            product.Quantity -= item.Value;
                            
                            // Проверка критического уровня
                            if (product.Quantity <= product.CriticalLevel)
                            {
                                OnLowStockWarning?.Invoke(product.Id, product.Name);
                                Log($"ВНИМАНИЕ: Критический уровень товара {product.Name} ({product.Quantity})", LogColor.Warning);
                            }
                        }
                    }
                }
                else
                {
                    Log($"Недостаточно товаров для заказа {order.OrderId}", LogColor.Warning);
                }
            }
            finally
            {
                _catalogMutex.ReleaseMutex();
            }
            
            return allItemsAvailable;
        }

        // Монитор таймаутов
        private async Task TimeoutMonitor()
        {
            while (_isRunning)
            {
                try
                {
                    await Task.Delay(5000); // Проверка каждые 5 секунд
                    
                    var now = DateTime.Now;
                    var ordersToCancel = new List<Order>();
                    
                    lock (_queueLock)
                    {
                        foreach (var order in _pendingOrders.Values)
                        {
                            if ((now - order.CreatedAt).TotalSeconds > 10) // 10 секунд таймаут
                            {
                                ordersToCancel.Add(order);
                            }
                        }
                        
                        // Удаление просроченных заказов
                        foreach (var order in ordersToCancel)
                        {
                            if (_pendingOrders.TryRemove(order.OrderId, out _))
                            {
                                order.Status = OrderStatus.Cancelled;
                                Log($"Заказ {order.OrderId} отменен по таймауту", LogColor.Warning);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Ошибка в мониторе таймаутов: {ex.Message}", LogColor.Error);
                }
            }
        }

        // Логирование
        private void Log(string message, LogColor color)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] {message}";
            
            Console.ForegroundColor = (ConsoleColor)color;
            Console.WriteLine(logMessage);
            Console.ResetColor();
            
            OnLogMessage?.Invoke(logMessage, color);
        }

        // Метод для отображения статуса системы
        public void DisplaySystemStatus()
        {
            Log($"=== СТАТУС СИСТЕМЫ ===", LogColor.Info);
            Log($"Товаров в каталоге: {_catalog.Count}", LogColor.Info);
            Log($"Заказов в очереди: {_orderQueue.Count}", LogColor.Info);
            Log($"Ожидающих заказов: {_pendingOrders.Count}", LogColor.Info);
        }
    }

    // Главный класс программы
    class BigProgram
    {
        public static async Task deMain()
        {
            Console.WriteLine("Запуск системы обработки заказов...");
            
            var system = new OrderProcessingSystem();
            
            // Подписка на события логирования
            system.OnLogMessage += (message, color) => {
                // Дополнительная обработка логов при необходимости
            };
            
            system.OnLowStockWarning += (productId, productName) => {
                Console.WriteLine($"🚨 НИЗКИЙ ЗАПАС: {productName} (ID: {productId})");
            };

            try
            {
                // Инициализация
                system.InitializeCatalog();
                
                // Запуск системы
                system.Start();
                
                // Ожидание пользовательского ввода для остановки
                Console.WriteLine("Нажмите любую клавишу для остановки...");
                Console.ReadKey();
                
                // Остановка системы
                await system.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка: {ex.Message}");
            }
            
            Console.WriteLine("Система остановлена. Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
}