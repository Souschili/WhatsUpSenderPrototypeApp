using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using static System.Net.Mime.MediaTypeNames;

namespace WhatsUpSenderPrototypeApp
{
    record class Recipient(string Name, string Number, string? Message);
    internal class Program
    {
        
           
        static async Task Main(string[] args)
        {
            var path = $"{AppContext.BaseDirectory}Phones.txt";

            // get numbers
            var recepientList = ParsePhones(path);


            var options = new ChromeOptions();
            options.AddArgument($"user-data-dir={AppContext.BaseDirectory}\\wa-profile");
           
            using IWebDriver driver = new ChromeDriver(options);
            // No implicit wait: mixing it with WebDriverWait causes unpredictable delays.
            // Each element we need is awaited explicitly below.
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
            var sendButton = By.XPath("//*[local-name()='title' and text()='ic-send-filled']/ancestor::button");
            // Popup "Phone number shared via url is invalid" has an OK button.
            // Matched by structure, not text, so it works in any UI language.
            var invalidNumberPopupButton = By.XPath("//div[@role='dialog']//button");

            driver.Navigate().GoToUrl("https://web.whatsapp.com");

            int sent = 0, failed = 0;
            foreach (var item in recepientList)
            {
                // One failed recipient must not stop the whole run
                try
                {
                    // Escape values so characters like '&', '#', '+' in the message don't break the URL
                    var phone = Uri.EscapeDataString(item.Number);
                    var text = Uri.EscapeDataString(item.Message ?? string.Empty);
                    var url = new Uri($"https://web.whatsapp.com/send?phone={phone}&text={text}");
                    driver.Navigate().GoToUrl(url);

                    // Poll until either the send button is ready or the "invalid number" popup appears.
                    // FindElements (plural) returns an empty list instead of throwing
                    // NoSuchElementException, so the debugger doesn't break on every poll.
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
                        // Close the popup so it doesn't block the next recipient
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
                    // Send button never appeared: number is not on WhatsApp or the chat didn't load
                    Console.WriteLine($"[FAIL] {item.Name} ({item.Number}): send button not found, number may not be on WhatsApp");
                    failed++;
                }
                catch (WebDriverException ex)
                {
                    // Any other browser error (click intercepted, stale element, etc.)
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
            var recepients= new List<Recipient>();
            // если нет файла уведомляем,надо продумать как выкидывать или завершать апп
            if (!File.Exists(path))
            {
                Console.WriteLine("Add phones list in project");
            }

            var lines=File.ReadAllLines(path);

            

            // create class object
            foreach (var line in lines)
            {
                var obj = line.Split(',',3);
                //name,phone,message
                recepients.Add(new Recipient(obj[0], obj[1].Trim().Replace(" ",""), obj[2]));
            }

            return recepients;
        }
    }
}
