using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class PrinterManager
{
    private readonly bool[] _printersAvailable;
    private readonly Random _random = new Random();
    private readonly object _lockObject = new object();

    public PrinterManager(int printerCount)
    {
        _printersAvailable = new bool[printerCount];

        for (int i = 0; i < printerCount; i++)
        {
            _printersAvailable[i] = true;
        }
    }

    public int RequestPrinter(int processId, SemaphoreSlim syncSemaphore)
    {
        syncSemaphore.Wait();

        try
        {
            lock (_lockObject)
            {
                for (int i = 0; i < _printersAvailable.Length; i++)
                {

                    if (_printersAvailable[i])
                    {
                        _printersAvailable[i] = false;
                        Console.WriteLine($"[Процесс {processId}] Получил принтер {i + 1}");
                        return i + 1;
                    }
                }
            }

            throw new InvalidOperationException("Не удалось найти доступный принтер");
        }
        finally{}
        /*
        finally
        {
            syncSemaphore.Release();
        }
        */
    }

    public void ReleasePrinter(int processId, int printerId, SemaphoreSlim syncSemaphore)
    {
        //syncSemaphore.Wait();

        try
        {
            if (printerId < 1 || printerId > _printersAvailable.Length)
            {
                throw new ArgumentException($"Неверный ID принтера: {printerId}");
            }

            if (_printersAvailable[printerId - 1])
            {
                throw new InvalidOperationException($"Принтер {printerId} уже свободен");
            }

            _printersAvailable[printerId - 1] = true;
            Console.WriteLine($"[Процесс {processId}] Освободил принтер {printerId}");
        }
        finally
        {
            syncSemaphore.Release();
        }
    }
}

class Program
{
    private static readonly int ProcessCount = 5;
    private static readonly int PrinterCount = 2;
    private static readonly Random Random = new Random();

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== СИМУЛЯЦИЯ РАБОТЫ ПРОЦЕССОВ С ПРИНТЕРАМИ ===");
        Console.WriteLine($"Количество процессов: {ProcessCount}");
        Console.WriteLine($"Количество принтеров: {PrinterCount}");
        Console.WriteLine("SemaphoreSlim вынесен в класс Program");
        Console.WriteLine("=" + new string('=', 50));

        var printerManager = new PrinterManager(PrinterCount);
        var tasks = new List<Task>();

        var syncSemaphore = new SemaphoreSlim(2, 2);

        for (int i = 1; i <= ProcessCount; i++)
        {
            int processId = i;
            tasks.Add(Task.Run(() => ProcessWork(printerManager, syncSemaphore, processId)));
        }

        await Task.WhenAll(tasks);

        Console.WriteLine("=" + new string('=', 50));
        Console.WriteLine("Все процессы завершили работу.");
    }

    static async Task ProcessWork(PrinterManager printerManager, SemaphoreSlim syncSemaphore, int processId)
    {
        try
        {
            await Task.Delay(Random.Next(100, 500));

            for (int i = 0; i < 1; i++)
            {
                try
                {
                    Console.WriteLine($"[Процесс {processId}] Ожидание доступа для запроса принтера...");

                    int printerId = printerManager.RequestPrinter(processId, syncSemaphore);

                    int printTime = Random.Next(1000, 3000);
                    Console.WriteLine($"[Процесс {processId}] Печатает на принтере {printerId} ({printTime}мс)...");
                    await Task.Delay(printTime);

                    //Console.WriteLine($"[Процесс {processId}] Ожидание доступа для освобождения принтера...");

                    printerManager.ReleasePrinter(processId, printerId, syncSemaphore);

                    await Task.Delay(Random.Next(500, 1500));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Процесс {processId}] Ошибка в цикле печати: {ex.Message}");
                }
            }

            Console.WriteLine($"[Процесс {processId}] Завершил все задачи печати.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Процесс {processId}] Критическая ошибка: {ex.Message}");
        }
    }
}