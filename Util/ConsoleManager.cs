using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PT200Emulator.Util
{
    public static class ConsoleManager
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private static bool consoleOpen = false;

        /// <summary>
        /// Öppnar konsolfönstret om det inte redan är öppet.
        /// </summary>
        public static void Open()
        {
            if (!consoleOpen)
            {
                AllocConsole();
                consoleOpen = true;

                // Koppla om standard output till den nya konsolen
                var stdOut = Console.OpenStandardOutput();
                var writer = new StreamWriter(stdOut) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.InputEncoding = System.Text.Encoding.UTF8;

                Console.WriteLine("Konsolen är nu öppen och redo att ta emot loggar.");
            }
        }

        /// <summary>
        /// Stänger konsolfönstret om det är öppet.
        /// </summary>
        public static void Close()
        {
            if (consoleOpen)
            {
                FreeConsole();
                consoleOpen = false;
            }
        }

        /// <summary>
        /// Växlar mellan öppet och stängt läge.
        /// </summary>
        public static void Toggle()
        {
            if (consoleOpen)
                Close();
            else
                Open();
        }
    }
}