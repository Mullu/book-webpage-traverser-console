using System.Text;

namespace BookWebPageScraper.Tests
{
    public static class FakeConsole
    {
        private static StringBuilder _output = new StringBuilder();
        private static TextWriter _originalOutput = Console.Out;

        public static string Output => _output.ToString();

        public static void StartCapture()
        {
            _output.Clear();
            Console.SetOut(new StringWriter(_output));
        }

        public static void StopCapture()
        {
            Console.SetOut(_originalOutput);
        }
    }
}
