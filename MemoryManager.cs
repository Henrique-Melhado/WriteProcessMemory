using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ReadWriteProcessMemory
{
    // Alias for clarity, though UInt64 is often used for 64-bit addresses
    using Pointer = System.UInt64;

    /// <summary>
    /// Manages reading and writing memory of an external process.
    /// Implements IDisposable for proper handle cleanup.
    /// </summary>
    public class MemoryManager : IDisposable
    {
        #region P/Invoke Declarations

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            ProcessAccessFlags dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [In] byte[] lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            int dwSize,
            MemoryProtection flNewProtect,
            out MemoryProtection lpflOldProtect);

        #endregion

        private IntPtr _processHandle = IntPtr.Zero;
        private int _processId;
        private Pointer _baseAddress;
        private int _imageSize;
        private string _fileName;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the MemoryManager class by process name.
        /// </summary>
        /// <param name="processName">The name of the process (e.g., "notepad").</param>
        /// <param name="moduleName">Optional name of the module (e.g., "client.dll"). If null, uses the main module.</param>
        /// <exception cref="ProcessNotFoundException">Thrown if the process is not found.</exception>
        /// <exception cref="ModuleNotFoundException">Thrown if the specified module is not found.</exception>
        /// <exception cref="MemoryOperationException">Thrown if OpenProcess fails.</exception>
        public MemoryManager(string processName, string moduleName = null)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                throw new ProcessNotFoundException($"Process with name '{processName}' not found.");
            }

            Initialize(processes[0], moduleName);
        }

        /// <summary>
        /// Initializes a new instance of the MemoryManager class by process ID.
        /// </summary>
        /// <param name="processId">The ID of the process.</param>
        /// <param name="moduleName">Optional name of the module (e.g., "client.dll"). If null, uses the main module.</param>
        /// <exception cref="ProcessNotFoundException">Thrown if the process is not found.</exception>
        /// <exception cref="ModuleNotFoundException">Thrown if the specified module is not found.</exception>
        /// <exception cref="MemoryOperationException">Thrown if OpenProcess fails.</exception>
        public MemoryManager(int processId, string moduleName = null)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                Initialize(process, moduleName);
            }
            catch (ArgumentException)
            {
                throw new ProcessNotFoundException($"Process with ID '{processId}' not found.");
            }
        }

        private void Initialize(Process process, string moduleName)
        {
            _processId = process.Id;
            _fileName = process.MainModule.FileName;

            // Get module information
            ProcessModule module = null;
            if (string.IsNullOrEmpty(moduleName))
            {
                module = process.MainModule;
            }
            else
            {
                foreach (ProcessModule m in process.Modules)
                {
                    if (m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        module = m;
                        break;
                    }
                }
            }

            if (module == null)
            {
                throw new ModuleNotFoundException($"Module '{moduleName}' not found in process '{process.ProcessName}'.");
            }

            _baseAddress = (Pointer)module.BaseAddress;
            _imageSize = module.ModuleMemorySize;

            // Request necessary access rights
            ProcessAccessFlags accessFlags = ProcessAccessFlags.VirtualMemoryRead | ProcessAccessFlags.VirtualMemoryWrite | ProcessAccessFlags.VirtualMemoryOperation | ProcessAccessFlags.QueryInformation;

            _processHandle = OpenProcess(accessFlags, false, _processId);

            if (_processHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new MemoryOperationException($"Failed to open process. Win32 Error: {error}");
            }
        }

        /// <summary>
        /// Gets the base address of the main module or the specified module.
        /// </summary>
        public Pointer BaseAddress => _baseAddress;

        /// <summary>
        /// Gets the ID of the opened process.
        /// </summary>
        public int ProcessId => _processId;

        /// <summary>
        /// Gets the handle to the opened process.
        /// </summary>
        public IntPtr ProcessHandle => _processHandle;

        /// <summary>
        /// Gets the file path of the opened process.
        /// </summary>
        public string FileName => _fileName;

        /// <summary>
        /// Reads a block of memory from the external process.
        /// </summary>
        /// <param name="address">The starting address to read from.</param>
        /// <param name="size">The number of bytes to read.</param>
        /// <returns>A byte array containing the read memory.</returns>
        /// <exception cref="MemoryOperationException">Thrown if ReadProcessMemory fails.</exception>
        public byte[] ReadMemory(Pointer address, int size)
        {
            if (_processHandle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(MemoryManager), "The process handle is closed.");
            }

            byte[] buffer = new byte[size];
            IntPtr bytesRead = IntPtr.Zero;

            // Removed unnecessary VirtualProtectEx calls
            bool success = ReadProcessMemory(_processHandle, (IntPtr)address, buffer, size, out bytesRead);

            if (!success || bytesRead.ToInt32() != size)
            {
                int error = Marshal.GetLastWin32Error();
                throw new MemoryOperationException($"Failed to read process memory at 0x{address:X}. Requested: {size} bytes, Read: {bytesRead.ToInt32()} bytes. Win32 Error: {error}");
            }

            return buffer;
        }

        /// <summary>
        /// Reads a value of a specified structure type from the external process memory.
        /// </summary>
        /// <typeparam name="T">The structure type to read.</typeparam>
        /// <param name="address">The starting address to read from.</param>
        /// <returns>The read structure.</returns>
        public T Read<T>(Pointer address) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buffer = ReadMemory(address, size);

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Reads a string from the external process memory.
        /// </summary>
        /// <param name="address">The starting address to read from.</param>
        /// <param name="size">The maximum number of bytes to read.</param>
        /// <param name="unicode">If true, reads a UTF-16 (wide) string; otherwise, reads an ANSI string.</param>
        /// <returns>The read string, truncated at the first null terminator.</returns>
        public string ReadString(Pointer address, int size = 255, bool unicode = true)
        {
            // Use System.Text.Encoding.Unicode for wide strings (UTF-16) on Windows
            Encoding encoding = unicode ? Encoding.Unicode : Encoding.Default;
            byte[] buffer = ReadMemory(address, size);

            string str = encoding.GetString(buffer);
            int pos = str.IndexOf('\0');

            // Truncate at the first null terminator
            if (pos > -1)
            {
                str = str.Substring(0, pos);
            }

            return str;
        }

        /// <summary>
        /// Writes a block of memory to the external process.
        /// </summary>
        /// <param name="address">The starting address to write to.</param>
        /// <param name="buffer">The byte array to write.</param>
        /// <exception cref="MemoryOperationException">Thrown if WriteProcessMemory fails.</exception>
        public void WriteMemory(Pointer address, byte[] buffer)
        {
            if (_processHandle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(MemoryManager), "The process handle is closed.");
            }

            IntPtr bytesWritten = IntPtr.Zero;

            // Removed unnecessary VirtualProtectEx calls
            bool success = WriteProcessMemory(_processHandle, (IntPtr)address, buffer, buffer.Length, out bytesWritten);

            if (!success || bytesWritten.ToInt32() != buffer.Length)
            {
                int error = Marshal.GetLastWin32Error();
                throw new MemoryOperationException($"Failed to write process memory at 0x{address:X}. Requested: {buffer.Length} bytes, Written: {bytesWritten.ToInt32()} bytes. Win32 Error: {error}");
            }
        }

        /// <summary>
        /// Writes a structure to the external process memory.
        /// </summary>
        /// <typeparam name="T">The structure type to write.</typeparam>
        /// <param name="address">The starting address to write to.</param>
        /// <param name="content">The structure content to write.</param>
        public void Write<T>(Pointer address, T content) where T : struct
        {
            int size = Marshal.SizeOf(content);
            byte[] buffer = new byte[size];

            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(content, pointer, false); // Use 'false' for fDeleteOld to avoid issues if content is not a class
                Marshal.Copy(pointer, buffer, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            WriteMemory(address, buffer);
        }

        /// <summary>
        /// Writes a string to the external process memory.
        /// </summary>
        /// <param name="address">The starting address to write to.</param>
        /// <param name="str">The string to write.</param>
        /// <param name="unicode">If true, writes a UTF-16 (wide) string; otherwise, writes an ANSI string.</param>
        public void WriteString(Pointer address, string str, bool unicode = true)
        {
            Encoding encoding = unicode ? Encoding.Unicode : Encoding.Default;
            byte[] buffer = encoding.GetBytes(str + '\0'); // Add null terminator for safety

            WriteMemory(address, buffer);
        }

        #region IDisposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects)
                }

                // Free unmanaged resources (unmanaged objects) and override finalizer
                if (_processHandle != IntPtr.Zero)
                {
                    CloseHandle(_processHandle);
                    _processHandle = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        // Override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~MemoryManager()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    #region Custom Exceptions

    public class MemoryOperationException : Exception
    {
        public MemoryOperationException(string message) : base(message) { }
        public MemoryOperationException(string message, Exception inner) : base(message, inner) { }
    }

    public class ProcessNotFoundException : MemoryOperationException
    {
        public ProcessNotFoundException(string message) : base(message) { }
    }

    public class ModuleNotFoundException : MemoryOperationException
    {
        public ModuleNotFoundException(string message) : base(message) { }
    }

    #endregion
}
