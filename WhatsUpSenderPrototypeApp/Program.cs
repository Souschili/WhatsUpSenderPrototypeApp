using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace WhatsUpSenderPrototypeApp
{
    record class Recipient(string Name, string Number, string Message);

    internal class Program
    {
        static void Main(string[] args)
        {
            // UTF-8 в консоли, чтобы корректно выводились русский текст и
            // азербайджанские буквы в именах (ə, ş, ğ)
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // И для ввода: стандартный текст может содержать азербайджанские буквы
            Console.InputEncoding = System.Text.Encoding.UTF8;

            // Глобальный перехват: любая непредвиденная ошибка (Chrome не запустился,
            // профиль занят другим окном Chrome, файл не читается и т.п.) выводится
            // в консоль, а не роняет приложение с закрытием окна
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("XƏTA: proqram dayandırıldı.");
                Console.WriteLine($"Səbəb: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Tərtibatçı üçün ətraflı məlumat:");
                Console.WriteLine(ex);
                Environment.ExitCode = 1;
                Console.WriteLine("Çıxmaq üçün Enter düyməsini basın.");
                Console.ReadLine();
            }
        }

        private static void Run()
        {
            var path = Path.Combine(AppContext.BaseDirectory, AppConstants.PhonesFileName);

            // Проверяем файл до запуска Chrome, чтобы не открывать браузер зря.
            // Исключение покажет глобальный catch в Main.
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Nömrələr faylı tapılmadı: {AppConstants.PhonesFileName}", path);
            }

            // Стандартный текст: подставляется тем получателям, у кого в файле нет своего сообщения.
            // Спрашиваем после проверки файла, чтобы пользователь не вводил текст зря.
            Console.WriteLine("Standart mesaj mətnini daxil edin (faylda mətni olmayan alıcılara göndəriləcək).");
            Console.WriteLine("Lazım deyilsə, sadəcə Enter basın:");
            var defaultMessage = (Console.ReadLine() ?? string.Empty).Trim();

            var recipients = ParsePhones(path, defaultMessage);
            if (recipients.Count == 0)
            {
                Console.WriteLine($"{AppConstants.PhonesFileName} faylında heç bir düzgün sətir yoxdur, mesaj göndəriləcək alıcı yoxdur.");
                Console.WriteLine("Sətir formatı: ad,telefon,mesaj (mesaj boş ola bilər, onda standart mətn göndərilir)");
                return;
            }

            var options = new ChromeOptions();
            // Постоянный профиль Chrome, чтобы вход в WhatsApp по QR сохранялся между запусками
            var profilePath = Path.Combine(AppContext.BaseDirectory, AppConstants.ChromeProfileFolderName);
            options.AddArgument($"user-data-dir={profilePath}");

            using IWebDriver driver = new ChromeDriver(options);
            // Неявное ожидание отключено: вместе с WebDriverWait оно даёт непредсказуемые задержки.
            // Нужные элементы ждём явно ниже.
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
            var wait = new WebDriverWait(driver, AppConstants.ElementWaitTimeout);
            // WhatsApp может перерисовать кнопку между FindElements и проверкой Displayed.
            // По умолчанию WebDriverWait игнорирует только NotFoundException, поэтому
            // StaleElementReferenceException добавляем явно: Until просто повторит проверку.
            wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
            var sendButton = By.XPath(AppConstants.SendButtonXPath);
            var invalidNumberPopupButton = By.XPath(AppConstants.InvalidNumberPopupButtonXPath);

            driver.Navigate().GoToUrl(AppConstants.WhatsAppWebUrl);

            // Ждём, пока WhatsApp Web загрузится (появится список чатов).
            // Если сессии нет, за это время нужно отсканировать QR-код.
            Console.WriteLine($"Siyahıdakı alıcı sayı: {recipients.Count}");
            Console.WriteLine("WhatsApp Web açılır...");
            Console.WriteLine($"Brauzerdə QR kod görünürsə, onu telefonla skan edin (WhatsApp > Əlaqəli cihazlar). Gözləmə müddəti: {AppConstants.LoginWaitTimeout.TotalMinutes} dəq.");
            var chatList = By.CssSelector(AppConstants.ChatListCssSelector);
            try
            {
                new WebDriverWait(driver, AppConstants.LoginWaitTimeout)
                    .Until(d => d.FindElements(chatList).Count > 0);
            }
            catch (WebDriverTimeoutException)
            {
                // Без входа все номера упадут по таймауту, поэтому рассылку не начинаем
                Console.WriteLine("WhatsApp Web-ə daxil olmaq alınmadı: QR kod skan edilmədi və ya səhifə yüklənmədi.");
                Console.WriteLine("Göndəriş ləğv edildi, heç bir mesaj göndərilmədi. Çıxmaq üçün Enter düyməsini basın və proqramı yenidən başladın.");
                Console.ReadLine();
                return;
            }
            Console.WriteLine("Giriş edildi, göndəriş başlayır.");
            Console.WriteLine();

            // Отдельное ожидание подтверждения отправки (кнопка должна исчезнуть после клика)
            var sendConfirmWait = new WebDriverWait(driver, AppConstants.SendConfirmTimeout);
            sendConfirmWait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));

            int sent = 0, failed = 0, notProcessed = 0;
            var pauseBeforeNext = false;

            // Ошибка по получателю: пишем в консоль и считаем
            void Fail(Recipient r, string reason)
            {
                Console.WriteLine($"[XƏTA]       {r.Name} ({r.Number}): {reason}");
                failed++;
            }

            for (int i = 0; i < recipients.Count; i++)
            {
                var item = recipients[i];
                // Случайная пауза только после успешной отправки: одинаковые интервалы
                // похожи на бота. После ошибки сообщение не ушло, ждать незачем.
                if (pauseBeforeNext)
                {
                    PauseBeforeNextRecipient();
                    pauseBeforeNext = false;
                }

                // Если Chrome закрыли, нет смысла перебирать остальные номера:
                // все они упадут с ошибкой. Останавливаем рассылку.
                if (!IsBrowserAlive(driver))
                {
                    notProcessed = recipients.Count - i;
                    Console.WriteLine($"Brauzer pəncərəsi bağlandı, göndəriş dayandırıldı. Emal olunmamış nömrələr: {notProcessed}.");
                    break;
                }

                // Ошибка на одном получателе не должна останавливать всю рассылку
                try
                {
                    // Кодируем текст, чтобы символы '&', '#', '+' в сообщении не ломали ссылку.
                    // Номер не кодируем: после ParsePhones в нём только цифры.
                    var text = Uri.EscapeDataString(item.Message);
                    var url = new Uri($"{AppConstants.WhatsAppSendUrl}?phone={item.Number}&text={text}");
                    driver.Navigate().GoToUrl(url);

                    // Ждём, пока появится либо кнопка отправки, либо окно "неверный номер".
                    // FindElements (во множественном числе) возвращает пустой список, а не бросает
                    // NoSuchElementException, поэтому отладчик не останавливается на каждой проверке.
                    IWebElement? invalidPopupOk = null;
                    var button = wait.Until(d =>
                    {
                        // У кнопки нет обычного disabled, WhatsApp блокирует её через aria-disabled,
                        // поэтому Enabled всегда true и проверять нужно атрибут
                        var send = d.FindElements(sendButton)
                                    .FirstOrDefault(b => b.Displayed && b.GetDomAttribute("aria-disabled") != "true");
                        if (send != null)
                            return send;

                        invalidPopupOk = d.FindElements(invalidNumberPopupButton).FirstOrDefault(b => b.Displayed);
                        return invalidPopupOk;
                    });

                    if (ReferenceEquals(button, invalidPopupOk))
                    {
                        // Закрываем окно, чтобы оно не мешало следующему получателю
                        button.Click();
                        Fail(item, $"WhatsApp nömrənin yanlış olduğunu bildirdi. Nömrəni {AppConstants.PhonesFileName} faylında yoxlayın.");
                        continue;
                    }

                    button.Click();

                    // Ждём подтверждения: кнопка отправки видна, только пока в поле есть текст.
                    // После отправки поле очищается и кнопка пропадает.
                    try
                    {
                        sendConfirmWait.Until(d => !d.FindElements(sendButton).Any(b => b.Displayed));
                    }
                    catch (WebDriverTimeoutException)
                    {
                        Fail(item, $"göndərmə düyməsi basıldı, amma {AppConstants.SendConfirmTimeout.TotalSeconds} saniyə ərzində mesajın getdiyi təsdiqlənmədi. Mesaj göndərilməmiş ola bilər.");
                        continue;
                    }

                    Console.WriteLine($"[GÖNDƏRİLDİ] {item.Name} ({item.Number})");
                    sent++;
                    pauseBeforeNext = true;
                }
                catch (WebDriverTimeoutException)
                {
                    // Кнопка отправки так и не появилась: номера нет в WhatsApp или чат не загрузился
                    Fail(item, $"çat {AppConstants.ElementWaitTimeout.TotalSeconds} saniyə ərzində açılmadı. Ola bilsin ki, bu nömrə WhatsApp-da yoxdur və ya internet zəifdir.");
                }
                catch (WebDriverException ex)
                {
                    // Любая другая ошибка браузера (клик перекрыт, элемент устарел и т.п.)
                    Fail(item, $"brauzer xətası, mesaj göndərilmədi. {FirstLine(ex.Message)}");
                }
                catch (Exception ex)
                {
                    // Любая не-Selenium ошибка на одном номере тоже не должна останавливать рассылку
                    Fail(item, $"gözlənilməz xəta, mesaj göndərilmədi. {ex.GetType().Name}: {FirstLine(ex.Message)}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Göndəriş başa çatdı.");
            Console.WriteLine($"Göndərildi: {sent}, xəta ilə: {failed}, emal olunmadı: {notProcessed}.");
            Console.WriteLine("Çıxmaq üçün Enter düyməsini basın.");
            Console.ReadLine();
        }


        // Случайная пауза в диапазоне из AppConstants
        private static void PauseBeforeNextRecipient()
        {
            var minMs = (int)AppConstants.MinDelayBetweenMessages.TotalMilliseconds;
            var maxMs = (int)AppConstants.MaxDelayBetweenMessages.TotalMilliseconds;
            var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(minMs, maxMs + 1));

            Console.WriteLine($"             Növbəti alıcıya qədər {delay.TotalSeconds:0} saniyə gözlənilir...");
            Thread.Sleep(delay);
        }

        // Проверяем, что браузер ещё открыт: запрос списка вкладок падает,
        // если Chrome закрыт (или закрыто последнее окно)
        private static bool IsBrowserAlive(IWebDriver driver)
        {
            try
            {
                return driver.WindowHandles.Count > 0;
            }
            catch (WebDriverException)
            {
                return false;
            }
        }

        // Сообщения Selenium многострочные (версии, сессия и т.п.),
        // пользователю показываем только первую строку
        private static string FirstLine(string message) =>
            message.Split('\n', 2)[0].Trim();

        private static IReadOnlyList<Recipient> ParsePhones(string path, string defaultMessage)
        {
            var recipients = new List<Recipient>();
            var lines = File.ReadAllLines(path);

            // Формат строки: имя,телефон,сообщение
            // Сообщение можно не указывать ("имя,телефон" или "имя,телефон,"),
            // тогда подставляется стандартный текст
            for (int i = 0; i < lines.Length; i++)
            {
                var lineNumber = i + 1;

                // Пустые строки (например, в конце файла) просто пропускаем
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var parts = lines[i].Split(AppConstants.PhonesFieldSeparator, AppConstants.PhonesFieldCount);
                if (parts.Length < AppConstants.PhonesRequiredFieldCount)
                {
                    var reason = $"{AppConstants.PhonesFileName}, sətir {lineNumber}: format yanlışdır, 'ad,telefon,mesaj' olmalıdır.";
                    Console.WriteLine($"[BURAXILDI]  {reason}");
                    continue;
                }

                var name = parts[0].Trim();
                // Оставляем только цифры: убираем пробелы, '+', '-', скобки
                var number = new string(parts[1].Where(char.IsDigit).ToArray());
                var ownMessage = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                var message = ownMessage.Length > 0 ? ownMessage : defaultMessage;

                if (number.Length == 0)
                {
                    var reason = $"{AppConstants.PhonesFileName}, sətir {lineNumber}: telefon nömrəsi göstərilməyib.";
                    Console.WriteLine($"[BURAXILDI]  {reason}");
                    continue;
                }

                // Своего текста нет, и стандартный не задан: отправлять нечего
                if (message.Length == 0)
                {
                    var reason = $"{AppConstants.PhonesFileName}, sətir {lineNumber}: mesaj mətni yoxdur və standart mətn daxil edilməyib.";
                    Console.WriteLine($"[BURAXILDI]  {reason}");
                    continue;
                }

                recipients.Add(new Recipient(name, number, message));
            }

            return recipients;
        }
    }
}
