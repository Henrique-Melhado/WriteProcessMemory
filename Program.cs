using System;

namespace ReadWriteProcessMemory
{
    class Program
    {
        // Use const for compile-time constants
        private const ulong BASE = 0x0195DEE8;
        private const ulong OffsetMoney1 = 0x10;
        // The other offsets are unused in the current example, but kept for context
        private const ulong OffsetMoney2 = 0x0;
        private const ulong OffsetMoney3 = 0x10;
        private const ulong OffsetMoney4 = 0x30;

        static void Main(string[] args)
        {
            Console.WriteLine("--- ReadWriteProcessMemory Example ---");
            
            // Use a try-catch block to handle potential exceptions like process not found
            try
            {
                // Use 'using' statement to ensure the process handle is closed (Dispose is called)
                using (var euroTruck = new MemoryManager("xxx"))
                {
                    Console.WriteLine($"Process Name: {euroTruck.FileName}");
                    Console.WriteLine($"Process ID: 0x{euroTruck.ProcessId:X8}");
                    Console.WriteLine($"Module Base Address: 0x{euroTruck.BaseAddress:X8}");

                    // Example Read Operation
                    ulong worldPointer = euroTruck.Read<ulong>(euroTruck.BaseAddress + BASE);
                    Console.WriteLine($"World Pointer Address: 0x{worldPointer:X8}");

                    ulong saldoDevedor = euroTruck.Read<ulong>(worldPointer + OffsetMoney1);
                    Console.WriteLine($"Saldo Devedor Value: 0x{saldoDevedor:X8}");
                    
                    // Example Write Operation (Commented out for safety in a generic example)
                    /*
                    ulong newSaldoDevedor = 0x99999999;
                    Console.WriteLine($"Writing new value: 0x{newSaldoDevedor:X8}");
                    euroTruck.Write<ulong>(worldPointer + OffsetMoney1, newSaldoDevedor);
                    */
                }
            }
            catch (ProcessNotFoundException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Please ensure the target process ('eurotrucks2') is running.");
            }
            catch (MemoryOperationException ex)
            {
                Console.WriteLine($"Memory Operation Error: {ex.Message}");
                Console.WriteLine("Check if the process has the necessary permissions (e.g., run as administrator).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }

            Console.WriteLine("\nPress Enter to exit...");
            Console.ReadLine();
        }
    }
}
