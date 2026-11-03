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
            var recipients = ParsePhones(path);

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
            // TODO: корректно завершать приложение, если файла нет
            if (!File.Exists(path))
            {
                Console.WriteLine("Add phones list in project");
            }

            var lines = File.ReadAllLines(path);

            // Формат строки: имя,телефон,сообщение
            foreach (var line in lines)
            {
                var parts = line.Split(',', 3);
                recipients.Add(new Recipient(parts[0], parts[1].Trim().Replace(" ", ""), parts[2]));
            }

            return recipients;
        }
    }
}
