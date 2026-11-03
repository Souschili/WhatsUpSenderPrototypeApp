using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace WhatsUpSenderPrototypeApp
{
    record class Recipient(string Name, string Number, string? Message);

    internal class Program
    {
        static void Main(string[] args)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Phones.txt");

            // Проверяем файл до запуска Chrome, чтобы не открывать браузер зря
            if (!File.Exists(path))
            {
                Console.WriteLine($"Файл с номерами не найден: {path}");
                Console.WriteLine("Скопируйте Phones.example.txt в Phones.txt и заполните его.");
                return;
            }

            var recipients = ParsePhones(path);
            if (recipients.Count == 0)
            {
                Console.WriteLine("В Phones.txt нет ни одной корректной строки.");
                return;
            }

            var options = new ChromeOptions();
            // Постоянный профиль Chrome, чтобы вход в WhatsApp по QR сохранялся между запусками
            options.AddArgument($"user-data-dir={Path.Combine(AppContext.BaseDirectory, "wa-profile")}");

            using IWebDriver driver = new ChromeDriver(options);
            // Неявное ожидание отключено: вместе с WebDriverWait оно даёт непредсказуемые задержки.
            // Нужные элементы ждём явно ниже.
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
            var sendButton = By.XPath("//*[local-name()='title' and text()='ic-send-filled']/ancestor::button");
            // В окне "Phone number shared via url is invalid" есть кнопка OK.
            // Ищем по разметке, а не по тексту, чтобы работало на любом языке интерфейса.
            var invalidNumberPopupButton = By.XPath("//div[@role='dialog']//button");

            driver.Navigate().GoToUrl("https://web.whatsapp.com");

            int sent = 0, failed = 0;
            foreach (var item in recipients)
            {
                // Ошибка на одном получателе не должна останавливать всю рассылку
                try
                {
                    // Кодируем значения, чтобы символы '&', '#', '+' в сообщении не ломали ссылку
                    var phone = Uri.EscapeDataString(item.Number);
                    var text = Uri.EscapeDataString(item.Message ?? string.Empty);
                    var url = new Uri($"https://web.whatsapp.com/send?phone={phone}&text={text}");
                    driver.Navigate().GoToUrl(url);

                    // Ждём, пока появится либо кнопка отправки, либо окно "неверный номер".
                    // FindElements (во множественном числе) возвращает пустой список, а не бросает
                    // NoSuchElementException, поэтому отладчик не останавливается на каждой проверке.
                    IWebElement? invalidPopupOk = null;
                    var button = wait.Until(d =>
                    {
                        var send = d.FindElements(sendButton).FirstOrDefault(b => b.Displayed && b.Enabled);
                        if (send != null)
                            return send;

                        invalidPopupOk = d.FindElements(invalidNumberPopupButton).FirstOrDefault(b => b.Displayed);
                        return invalidPopupOk;
                    });

                    if (ReferenceEquals(button, invalidPopupOk))
                    {
                        // Закрываем окно, чтобы оно не мешало следующему получателю
                        button.Click();
                        Console.WriteLine($"[FAIL] {item.Name} ({item.Number}): invalid phone number");
                        failed++;
                        continue;
                    }

                    button.Click();

                    Console.WriteLine($"[OK]   {item.Name} ({item.Number})");
                    sent++;
                    Thread.Sleep(3000);
                }
                catch (WebDriverTimeoutException)
                {
                    // Кнопка отправки так и не появилась: номера нет в WhatsApp или чат не загрузился
                    Console.WriteLine($"[FAIL] {item.Name} ({item.Number}): send button not found, number may not be on WhatsApp");
                    failed++;
                }
                catch (WebDriverException ex)
                {
                    // Любая другая ошибка браузера (клик перекрыт, элемент устарел и т.п.)
                    Console.WriteLine($"[FAIL] {item.Name} ({item.Number}): {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine($"Done. Sent: {sent}, failed: {failed}. Press Enter to exit.");
            Console.ReadLine();
            driver.Quit();
        }

        private static IReadOnlyCollection<Recipient> ParsePhones(string path)
        {
            var recipients = new List<Recipient>();
            var lines = File.ReadAllLines(path);

            // Формат строки: имя,телефон,сообщение
            for (int i = 0; i < lines.Length; i++)
            {
                var lineNumber = i + 1;

                // Пустые строки (например, в конце файла) просто пропускаем
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var parts = lines[i].Split(',', 3);
                if (parts.Length < 3)
                {
                    Console.WriteLine($"Строка {lineNumber} пропущена: ожидается формат 'имя,телефон,сообщение'.");
                    continue;
                }

                var name = parts[0].Trim();
                // Оставляем только цифры: убираем пробелы, '+', '-', скобки
                var number = new string(parts[1].Where(char.IsDigit).ToArray());
                var message = parts[2].Trim();

                if (number.Length == 0 || message.Length == 0)
                {
                    Console.WriteLine($"Строка {lineNumber} пропущена: пустой телефон или сообщение.");
                    continue;
                }

                recipients.Add(new Recipient(name, number, message));
            }

            return recipients;
        }
    }
}
