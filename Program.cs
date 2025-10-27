using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lab4
{
    // Общие требования
    // 1. Обязательное применение примитивов синхронизации: В решениях обязательно использовать указанные в работе примитивы.
    // 2. Консольное приложение: Реализация в виде консольного приложения с наглядным выводом операций.
    // 3. Обработка ошибок: Корректная обработка исключительных ситуаций в многопоточной среде.
    // Технологии: C#, Thread/Task, Semaphore, SemaphoreSlim, Mutex, Monitor, конкурентные коллекции.
    //
    // Неделимая рассылка.
    // Предположим, что один процесс-производитель и n процессов- потребителей разделяют общий буфер.
    // Производитель помещает в буфер сообщения, потребители извлекают их.
    // Каждое сообщение, помещенное в буфер производителем, должны получить все n потребителей,
    // и только после этого производитель сможет поместить в буфер следующее сообщение.
    // Выполнение продолжается, пока производитель не отошлет все заданные сообщения. 
    class Program
    {
        // Инициализация переменных
        private static int bufferSize = 32;
        private static bool showBuffer = true;
        private static int readersAmount = 8;
        private static int writesAmount = 4;

        private static char[] buffer = new char[bufferSize];
        private static readonly string stringAlphabet = "QWERTYUIOPASDFGHJKLZXCVBNMqwertyuiopasdfghjklzxcvbnm1234567890";
        private static readonly char[] alphabet = stringAlphabet.ToCharArray();

        // Синхронизация через Monitor
        private static readonly object locker = new object();
        // remainingReaders == 0 -> нет доступного сообщения (читатели ждут)
        // remainingReaders == readersAmount -> есть новое сообщение, которое ещё нужно прочитать
        private static int remainingReaders = 0;

        private static void Reader(int id)
        {
            try
            {
                for (int iter = 0; iter < writesAmount; iter++)
                {
                    string localCopy;
                    lock (locker)
                    {
                        // Ждём появления нового сообщения
                        while (remainingReaders == 0)
                        {
                            Monitor.Wait(locker);
                        }

                        // Читаем буфер (копируем чтобы сократить время нахождения в блокировке)
                        localCopy = new string(buffer);

                        // Отмечаем факт чтения одним читателем
                        remainingReaders--;

                        // Если это был последний читатель — уведомляем писателя
                        if (remainingReaders == 0)
                        {
                            Monitor.Pulse(locker);
                        }
                    }

                    // Вывод вне блокировки
                    if (showBuffer)
                        Console.WriteLine($"Читатель {id} прочитал: {localCopy}");
                    else
                        Console.WriteLine($"Читатель {id} прочитал сообщение #{iter + 1}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Ошибка в потоке Читатель {id}: {e.Message}");
            }
        }

        private static void Writer()
        {
            try
            {
                for (int write = 0; write < writesAmount; write++)
                {
                    lock (locker)
                    {
                        // Ждём, пока предыдущий пакет будет полностью прочитан
                        while (remainingReaders > 0)
                        {
                            Monitor.Wait(locker);
                        }

                        // Заполняем буфер случайными символами
                        for (int i = 0; i < bufferSize; i++)
                        {
                            buffer[i] = alphabet[Random.Shared.Next(alphabet.Length)];
                        }

                        if (showBuffer)
                            Console.WriteLine($"Писатель записал #{write + 1}: {new string(buffer)}");
                        else
                            Console.WriteLine($"Писатель записал сообщение #{write + 1}");

                        // Устанавливаем счётчик читателей и будим всех читателей
                        remainingReaders = readersAmount;
                        Monitor.PulseAll(locker);

                        // Ждём, пока все читатели не прочитают это сообщение
                        while (remainingReaders > 0)
                        {
                            Monitor.Wait(locker);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в потоке Писатель: {ex.Message}");
            }
        }
        private static void daMain() {
            // Пересоздаём буфер с учётом возможного изменения размера
            buffer = new char[bufferSize];

            // Создаём и запускаем потоки
            Thread writerThread = new Thread(Writer) { Name = "Писатель" };
            Thread[] readerThreads = new Thread[readersAmount];

            for (int i = 0; i < readersAmount; i++)
            {
                int id = i + 1;
                readerThreads[i] = new Thread(() => Reader(id)) { Name = $"Читатель{id}" };
            }

            // Старт
            for (int i = 0; i < readersAmount; i++) readerThreads[i].Start();
            writerThread.Start();

            // Ждём завершения
            writerThread.Join();
            for (int i = 0; i < readersAmount; i++) readerThreads[i].Join();

            Console.WriteLine("Все операции завершены.");
        }

        public static async Task Main(string[] args)  // bufferSize showBuffer readersAmount writesAmount
        {
            // Парсинг аргументов
            if (args.Length == 4)
            {
                try
                {
                    bufferSize = int.Parse(args[0]);
                    showBuffer = bool.Parse(args[1]);
                    readersAmount = int.Parse(args[2]);
                    writesAmount = int.Parse(args[3]);
                }
                catch
                {
                    bufferSize = 32;
                    showBuffer = true;
                    readersAmount = 8;
                    writesAmount = 4;
                }
                daMain();
            } else if (args.Length == 1) {
                if (int.Parse(args[0]) == 1) {  // запуск общей программы
                    await OrderProcessingSystem.BigProgram.deMain();
                } else {
                    daMain();
                }
            }
        }
    }
}