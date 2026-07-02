using System.Text;

namespace CodeGenerationEngine.Helper;

public static class InteractiveConsole
{
    public static string ReadCommand()
    {
        var buffer = new StringBuilder();

        Console.Write(" CodeGenerationEngine >> ");

        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;

                    Console.Write("\b \b");

                    RenderSuggestions(buffer.ToString());
                }

                continue;
            }

            buffer.Append(key.KeyChar);
            Console.Write(key.KeyChar);

            RenderSuggestions(buffer.ToString());
        }
    }

    private static void RenderSuggestions(string currentText)
    {
        var suggestions = CommandSuggestionService.GetSuggestions(currentText).ToList();

        var left = Console.CursorLeft;
        var top = Console.CursorTop;

        Console.SetCursorPosition(0, top + 1);

        Console.Write(new string(' ', Console.WindowWidth));

        if (suggestions.Any())
        {
            Console.SetCursorPosition(0, top + 1);

            Console.ForegroundColor = ConsoleColor.DarkGray;

            Console.Write(string.Join(" | ", suggestions));

            Console.ResetColor();
        }

        Console.SetCursorPosition(left, top);
    }
}