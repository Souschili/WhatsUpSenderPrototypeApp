namespace WhatsUpSenderPrototypeApp;

// Все настройки приложения в одном месте: меняем здесь, а не по коду
internal static class AppConstants
{
    // Файлы и папки (лежат рядом с exe)
    public const string PhonesFileName = "Phones.txt";
    public const string ChromeProfileFolderName = "wa-profile";

    // Адреса WhatsApp Web
    public const string WhatsAppWebUrl = "https://web.whatsapp.com";
    public const string WhatsAppSendUrl = "https://web.whatsapp.com/send";

    // Тайминги
    public static readonly TimeSpan ElementWaitTimeout = TimeSpan.FromSeconds(60);
    // Время на загрузку WhatsApp Web и сканирование QR-кода при первом входе
    public static readonly TimeSpan LoginWaitTimeout = TimeSpan.FromMinutes(2);
    // Сколько ждать, пока отправленное сообщение уйдёт (кнопка отправки исчезнет)
    public static readonly TimeSpan SendConfirmTimeout = TimeSpan.FromSeconds(30);
    // Случайная пауза между получателями: выбирается в этом диапазоне
    public static readonly TimeSpan MinDelayBetweenMessages = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelayBetweenMessages = TimeSpan.FromSeconds(15);

    // Боковая панель со списком чатов: появляется только после входа.
    // Ищем по id, а не по тексту, чтобы не зависеть от языка интерфейса.
    public const string ChatListCssSelector = "#pane-side";

    // Локаторы элементов страницы (XPath).
    // Если WhatsApp поменяет разметку, править нужно только здесь.
    public const string SendButtonXPath =
        "//*[local-name()='title' and text()='ic-send-filled']/ancestor::button";

    // В окне "Phone number shared via url is invalid" есть кнопка OK.
    // Ищем по разметке, а не по тексту, чтобы работало на любом языке интерфейса.
    public const string InvalidNumberPopupButtonXPath = "//div[@role='dialog']//button";

    // Разбор строк файла с номерами: имя,телефон,сообщение
    public const char PhonesFieldSeparator = ',';
    public const int PhonesFieldCount = 3;
    // Обязательны только имя и телефон, сообщение можно не указывать
    public const int PhonesRequiredFieldCount = 2;
}
