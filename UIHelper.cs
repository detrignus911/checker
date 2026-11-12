using System;

namespace opentask
{
    static class UI
    {
        public static void DrawHeader(string title, string subtitle, int width)
        {
            Console.Clear();
            if (width < 40) width = 40;
            string border = new string('═', width - 2);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔" + border + "╗");
            Console.WriteLine("║" + CenterText(title, width - 2) + "║");
            Console.WriteLine("╠" + border + "╣");
            Console.WriteLine("║" + CenterText(subtitle, width - 2) + "║");
            Console.WriteLine("╚" + border + "╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void DrawMenu(string[] items, int selected)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (i == selected)
                {
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                Console.WriteLine($" {i + 1}) {items[i]}");
            }
            Console.ResetColor();
            Console.WriteLine("\n↑/↓ — навигация, Enter — выбрать, Q — выход\n");
        }

        public static void WriteLine(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        public static void WaitKey(string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(text);
            Console.ResetColor();
            Console.ReadKey(true);
        }

        public static void PrintProgressBar(int percent, int width = 40)
        {
            int filled = (int)(percent / 100.0 * width);
            Console.CursorLeft = 0;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[");
            Console.Write(new string('█', filled));
            Console.Write(new string(' ', width - filled));
            Console.Write($"] {percent}%");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static string CenterText(string text, int width)
        {
            if (text.Length >= width) return text.Substring(0, width);
            int pad = (width - text.Length) / 2;
            return new string(' ', pad) + text + new string(' ', width - text.Length - pad);
        }
    }

    public static class StringExtensions
    {
        public static string CenterString(this string str, int width)
        {
            if (str.Length >= width) return str;
            int leftPadding = (width - str.Length) / 2;
            int rightPadding = width - str.Length - leftPadding;
            return new string(' ', leftPadding) + str + new string(' ', rightPadding);
        }
    }
}