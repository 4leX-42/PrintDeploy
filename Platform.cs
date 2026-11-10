using System;
using System.Runtime.InteropServices;

namespace InstaladorImpresoras
{
    /// <summary>
    /// Arquitectura real del equipo. En Windows ARM64 el .exe (AnyCPU) corre emulado como x64 y
    /// PROCESSOR_ARCHITECTURE dice AMD64: hay que preguntar a IsWow64Process2 por la máquina nativa.
    /// Los drivers de impresora se cargan en el spooler nativo, así que en ARM64 un driver x64 no vale.
    /// PRINTDEPLOY_ARCH=ARM64 o X64 fuerza el valor (pruebas).
    /// </summary>
    static class Platform
    {
        const ushort MachineArm64 = 0xAA64;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        static readonly Lazy<bool> arm64 = new Lazy<bool>(() =>
        {
            var forced = Environment.GetEnvironmentVariable("PRINTDEPLOY_ARCH");
            if (!string.IsNullOrEmpty(forced)) return forced.Equals("ARM64", StringComparison.OrdinalIgnoreCase);
            try { return IsWow64Process2(GetCurrentProcess(), out _, out var native) && native == MachineArm64; }
            catch (EntryPointNotFoundException) { return false; }   // Windows anterior a 10 1709: no hay ARM64
        });

        public static bool IsArm64 => arm64.Value;
    }
}
