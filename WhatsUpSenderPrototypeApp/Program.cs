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

            driver.Navigate().GoToUrl("https://web.whatsapp.com");
            foreach (var item in recepientList)
            {
                // Escape values so characters like '&', '#', '+' in the message don't break the URL
                var phone = Uri.EscapeDataString(item.Number);
                var text = Uri.EscapeDataString(item.Message ?? string.Empty);
                var url = new Uri($"https://web.whatsapp.com/send?phone={phone}&text={text}");
                driver.Navigate().GoToUrl(url);

                // Poll until the send button is visible and enabled.
                // FindElements (plural) returns an empty list instead of throwing
                // NoSuchElementException, so the debugger doesn't break on every poll.
                var button = wait.Until(d => d.FindElements(sendButton)
                                              .FirstOrDefault(b => b.Displayed && b.Enabled));
                button.Click();
               Thread.Sleep(3000);
            }
           
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
