namespace VgcStatus
{
    /// <summary>
    /// Простейшая локализация: для каждой строки задаётся русский и английский
    /// вариант, Loc.Of выбирает нужный по текущему языку интерфейса.
    /// </summary>
    internal static class Loc
    {
        public static bool En;

        public static string Of(string ru, string en)
        {
            return En ? en : ru;
        }
    }
}
